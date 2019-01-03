﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Internal;
using Squared.Threading;
using Squared.Util;

namespace Squared.Render {
    public static class RenderCoordinatorExtensions {
        public static void ApplyChangesAfterPresent (this GraphicsDeviceManager gdm, RenderCoordinator rc) {
            // HACK: Wait until rendering has finished, then reset the device on the main thread
            var sc = SynchronizationContext.Current;
            rc.AfterPresent(() => {
                sc.Post((_) => gdm.ApplyChanges(), null);
            });
        }
    }

    public class RenderCoordinator : IDisposable {
        struct DrawTask : IWorkItem {
            public readonly Action<Frame> Callback;
            public readonly Frame Frame;

            public DrawTask (Action<Frame> callback, Frame frame) {
                Callback = callback;
                Frame = frame;
            }

            public void Execute () {
                lock (Frame)
                    Callback(Frame);
            }
        }

        public readonly RenderManager Manager;

        /// <summary>
        /// If set to false, threads will not be used for rendering.
        /// </summary>
        public bool EnableThreading = true;
        /// <summary>
        /// You must acquire this lock before applying changes to the device, creating objects, or loading content.
        /// </summary>
        public readonly object CreateResourceLock;
        /// <summary>
        /// You must acquire this lock before rendering or resetting the device.
        /// </summary>
        public readonly object UseResourceLock;
        /// <summary>
        /// This lock is held during frame preparation.
        /// </summary>
        public readonly object PrepareLock = new object();

        // Held during paint
        private readonly object DrawLock = new object();

        private bool _Running = true;
#if SDL2 // Disable threading -flibit
        // 8 months later and I continue to say: NOPE. -flibit
        private bool _ActualEnableThreading = false;
#else
        private bool _ActualEnableThreading = true;
#endif
        private object _FrameLock = new object();
        private Frame  _FrameBeingPrepared = null;

        private readonly IGraphicsDeviceService DeviceService;

        internal bool SynchronousDrawsEnabled = true;

        private volatile bool IsResetting;
        private volatile bool FirstFrameSinceReset;

        private readonly Func<bool> _SyncBeginDraw;
        private readonly Action _SyncEndDraw;
        private readonly List<IDisposable> _PendingDisposes = new List<IDisposable>();
        private readonly ManualResetEvent _SynchronousDrawFinishedSignal = new ManualResetEvent(true);

        public readonly Stopwatch
            WorkStopwatch = new Stopwatch(),
            WaitStopwatch = new Stopwatch(),
            BeforePrepareStopwatch = new Stopwatch(),
            BeforePresentStopwatch = new Stopwatch();

        // Used to detect re-entrant painting (usually means that an
        //  exception was thrown on the render thread)
        private int _SynchronousDrawIsActive = 0;
        // Sometimes a new paint can be issued while we're blocked on
        //  a wait handle, because waits pump messages. We need to
        //  detect this and ensure another draw does not begin.
        private int _InsideDrawOperation = 0;

        // Lost devices can cause things to go horribly wrong if we're 
        //  using multithreaded rendering
        private bool _DeviceLost = false, _DeviceIsDisposed = false;

        private readonly ConcurrentQueue<Action> BeforePrepareQueue = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<Action> BeforePresentQueue = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<Action> AfterPresentQueue = new ConcurrentQueue<Action>();

        private readonly ManualResetEvent PresentBegunSignal = new ManualResetEvent(false);
        private long PresentBegunWhen = 0;

        public readonly ThreadGroup ThreadGroup;
        private readonly WorkQueue<DrawTask> DrawQueue;

        public event EventHandler DeviceReset, DeviceChanged;

        public bool IsDisposed { get; private set; }

        private long TimeOfLastResetOrDeviceChange = 0;

        /// <summary>
        /// Constructs a render coordinator.
        /// </summary>
        /// <param name="manager">The render manager responsible for creating frames and dispatching them to the graphics device.</param>
        /// <param name="synchronousBeginDraw">The function responsible for synchronously beginning a rendering operation. This will be invoked on the rendering thread.</param>
        /// <param name="synchronousEndDraw">The function responsible for synchronously ending a rendering operation and presenting it to the screen. This will be invoked on the rendering thread.</param>
        public RenderCoordinator (
            RenderManager manager, 
            Func<bool> synchronousBeginDraw, Action synchronousEndDraw
        ) {
            Manager = manager;
            ThreadGroup = manager.ThreadGroup;
            UseResourceLock = manager.UseResourceLock;
            CreateResourceLock = manager.CreateResourceLock;

            _SyncBeginDraw = synchronousBeginDraw;
            _SyncEndDraw = synchronousEndDraw;

            DrawQueue = ThreadGroup.GetQueueForType<DrawTask>();

            RegisterForDeviceEvents();
        }

        /// <summary>
        /// Constructs a render coordinator. A render manager and synchronous draw methods are automatically provided for you.
        /// </summary>
        /// <param name="deviceService"></param>
        public RenderCoordinator (
            IGraphicsDeviceService deviceService, Thread mainThread, ThreadGroup threadGroup,
            Func<bool> synchronousBeginDraw = null, Action synchronousEndDraw = null
        ) {
            DeviceService = deviceService;
            ThreadGroup = threadGroup;
            Manager = new RenderManager(deviceService.GraphicsDevice, mainThread, ThreadGroup);
            UseResourceLock = Manager.UseResourceLock;
            CreateResourceLock = Manager.CreateResourceLock;

            _SyncBeginDraw = synchronousBeginDraw ?? DefaultBeginDraw;
            _SyncEndDraw = synchronousEndDraw ?? DefaultEndDraw;

            DrawQueue = ThreadGroup.GetQueueForType<DrawTask>();

            RegisterForDeviceEvents();

            deviceService.DeviceCreated += DeviceService_DeviceCreated;
        }

        private void DeviceService_DeviceCreated (object sender, EventArgs e) {
            if (DeviceService.GraphicsDevice != Manager.DeviceManager.Device) {
                TimeOfLastResetOrDeviceChange = Time.Ticks;

                Manager.ChangeDevice(DeviceService.GraphicsDevice);
                RegisterForDeviceEvents();

                if (DeviceChanged != null)
                    DeviceChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Queues an operation to occur immediately before prepare operations begin.
        /// </summary>
        public void BeforePrepare (Action action) {
            BeforePrepareQueue.Enqueue(action);
        }

        /// <summary>
        /// Queues an operation to occur immediately before Present, after all drawing
        ///  commands have been issued.
        /// </summary>
        public void BeforePresent (Action action) {
            BeforePresentQueue.Enqueue(action);
        }

        /// <summary>
        /// Queues an operation to occur immediately after Present.
        /// </summary>
        public void AfterPresent (Action action) {
            AfterPresentQueue.Enqueue(action);
        }

        private void RegisterForDeviceEvents () {
            Device.DeviceResetting += OnDeviceResetting;
            Device.DeviceReset += OnDeviceReset;
            Device.DeviceLost += OnDeviceLost;
            Device.Disposing += OnDeviceDisposing;
        }

        protected bool DefaultBeginDraw () {
            if (IsDisposed)
                return false;

            if (Device.GraphicsDeviceStatus == GraphicsDeviceStatus.Normal) {
                RenderManager.ResetDeviceState(Device);
                return true;
            } else if (!_Running)
                return false;

            return false;
        }

        protected void DefaultEndDraw () {
            if (IsDisposed)
                return;

            var viewport = Device.Viewport;
            Device.Present(
#if !SDL2 // Ignore verbose Present() overload -flibit
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                IntPtr.Zero
#endif
            );
        }

        protected void OnDeviceDisposing (object sender, EventArgs args) {
            _DeviceIsDisposed = true;
            _DeviceLost = true;
        }

        protected void OnDeviceLost (object sender, EventArgs args) {
            TimeOfLastResetOrDeviceChange = Time.Ticks;
            FirstFrameSinceReset = true;
            _DeviceLost = true;
        }

        // We must acquire both locks before resetting the device to avoid letting the reset happen during a paint or content load operation.
        protected void OnDeviceResetting (object sender, EventArgs args) {
            TimeOfLastResetOrDeviceChange = Time.Ticks;
            FirstFrameSinceReset = true;

            if (!IsResetting) {
                IsResetting = true;

                Monitor.Enter(DrawLock);
                Monitor.Enter(CreateResourceLock);
                Monitor.Enter(UseResourceLock);
            }

            UniformBinding.HandleDeviceReset();
        }

        protected void EndReset () {
            if (Device == null) {
            }

            if (Device.IsDisposed) {
                _DeviceIsDisposed = true;
                return;
            }

            if (IsResetting) {
                Monitor.Exit(UseResourceLock);
                Monitor.Exit(CreateResourceLock);
                Monitor.Exit(DrawLock);

                IsResetting = false;
                FirstFrameSinceReset = true;
            }
        }

        protected void OnDeviceReset (object sender, EventArgs args) {
            TimeOfLastResetOrDeviceChange = Time.Ticks;

            if (IsResetting)
                EndReset();

            if (DeviceReset != null)
                DeviceReset(this, EventArgs.Empty);
        }
                
        private void WaitForPendingWork () {
            if (IsDisposed)
                return;

            var working = WorkStopwatch.IsRunning;
            if (working)
                WorkStopwatch.Stop();

            WaitStopwatch.Start();
            try {
                DrawQueue.WaitUntilDrained();
            } catch (DeviceLostException) {
                _DeviceLost = true;
            } catch (ObjectDisposedException) {
                if (Device.IsDisposed)
                    _Running = false;
                else
                    throw;
            }
            WaitStopwatch.Stop();

            if (working)
                WorkStopwatch.Start();
        }

        private bool WaitForActiveSynchronousDraw () {
            if (IsDisposed)
                return false;

            _SynchronousDrawFinishedSignal.WaitOne();
            return true;
        }

        public bool WaitForActiveDraws () {
            return WaitForActiveDraw() &&
                WaitForActiveSynchronousDraw();
        }

        internal bool WaitForActiveDraw () {
            if (_ActualEnableThreading) {
                DrawQueue.WaitUntilDrained();
            } else
                return false;

            return true;
        }

        public Frame BeginFrame () {
            return _FrameBeingPrepared = Manager.CreateFrame();
        }

        private bool ShouldSuppressResetRelatedDrawErrors {
            get {
                if (FirstFrameSinceReset || _DeviceIsDisposed || _DeviceLost)
                    return true;

                // HACK
                var now = Time.Ticks;
                if ((now - TimeOfLastResetOrDeviceChange) < TimeSpan.FromMilliseconds(200).Ticks)
                    return true;

                return false;
            }
        }

        public bool BeginDraw () {
            var ffsr = FirstFrameSinceReset;

            if (IsResetting || _DeviceIsDisposed) {
                EndReset();
                if (_DeviceIsDisposed) {
                    _DeviceIsDisposed = Device.IsDisposed;
                    if (!_DeviceIsDisposed)
                        _DeviceLost = false;
                }
                return false;
            }
            if (IsDisposed)
                return false;
            if (_InsideDrawOperation > 0)
                return false;

            try {
                WaitForActiveSynchronousDraw();
                WaitForActiveDraw();
            } catch (Exception exc) {
                if (ShouldSuppressResetRelatedDrawErrors) {
                    if (
                        (exc is ObjectDisposedException) || 
                        (exc is InvalidOperationException) || 
                        (exc is NullReferenceException)
                    )
                        return false;
                }

                throw;
            }

            Interlocked.Increment(ref _InsideDrawOperation);
            try {
                _ActualEnableThreading = EnableThreading;

                PresentBegunSignal.Reset();

                bool result;
                if (_Running) {
                    if (DoThreadedIssue)
                        result = true;
                    else
                        result = _SyncBeginDraw();
                } else {
                    result = false;
                }

                if (ffsr && FirstFrameSinceReset && result) {
                    FirstFrameSinceReset = false;
                    UniformBinding.CollectGarbage();
                }

                return result;
            } finally {
                Interlocked.Decrement(ref _InsideDrawOperation);
            }
        }

        protected void CheckMainThread (bool allowThreading) {
            if (allowThreading)
                return;

            if (Thread.CurrentThread != Manager.MainThread)
                throw new ThreadStateException("Function running off main thread in single threaded mode");
        }

        protected void PrepareFrame (Frame frame, bool threaded) {
            if (DoThreadedPrepare)
                Monitor.Enter(PrepareLock);

            CheckMainThread(DoThreadedPrepare && threaded);

            try {
                RunBeforePrepareHandlers();
                Manager.ResetBufferGenerators(frame.Index);
                frame.Prepare(DoThreadedPrepare && threaded);
            } finally {
                if (DoThreadedPrepare)
                    Monitor.Exit(PrepareLock);
            }
        }

        /// <summary>
        /// Finishes preparing the current Frame and readies it to be sent to the graphics device for rendering.
        /// </summary>
        protected void PrepareNextFrame (Frame newFrame, bool threaded) {
            PresentBegunSignal.Reset();

            if (newFrame != null)
                PrepareFrame(newFrame, threaded);
        }
        
        protected bool DoThreadedPrepare {
            get {
                return _ActualEnableThreading;
            }
        }
        
        protected bool DoThreadedIssue { 
            get {
                return _ActualEnableThreading;
            }
        }

        public void EndDraw () {
            if (IsDisposed)
                return;

            Interlocked.Increment(ref _InsideDrawOperation);
            try {
                Frame newFrame;
                lock (_FrameLock)
                    newFrame = Interlocked.Exchange(ref _FrameBeingPrepared, null);

                PrepareNextFrame(newFrame, true);
            
                if (_Running) {
                    if (DoThreadedIssue) {
                        lock (UseResourceLock)
                        if (!_SyncBeginDraw())
                            return;

                        DrawQueue.Enqueue(new DrawTask(ThreadedDraw, newFrame));
                        ThreadGroup.NotifyQueuesChanged();
                    } else {
                        ThreadedDraw(newFrame);
                    }

                    if (_DeviceLost) {
                        WaitForActiveDraw();

                        _DeviceLost = IsDeviceLost;
                    }
                }
            } finally {
                Interlocked.Decrement(ref _InsideDrawOperation);
            }
        }

        private void RenderFrame (Frame frame, bool acquireLock) {
            if (acquireLock)
                Monitor.Enter(UseResourceLock);

            try {
                // In D3D builds, this checks to see whether PIX is attached right now
                //  so that if it's not, we don't waste cpu time/gc pressure on trace messages
                Tracing.RenderTrace.BeforeFrame();

                if (frame != null) {
                    _DeviceLost |= IsDeviceLost;

                    if (!_DeviceLost)
                        frame.Draw();
                }
            } finally {
                if (acquireLock)
                    Monitor.Exit(UseResourceLock);
            }

            _DeviceLost |= IsDeviceLost;
        }

        protected void RunBeforePrepareHandlers () {
            BeforePrepareStopwatch.Start();

            while (BeforePrepareQueue.Count > 0) {
                Action beforePrepare;
                if (!BeforePrepareQueue.TryDequeue(out beforePrepare))
                    continue;

                beforePrepare();
            }

            BeforePrepareStopwatch.Stop();
        }

        protected void RunBeforePresentHandlers () {
            BeforePresentStopwatch.Start();

            while (BeforePresentQueue.Count > 0) {
                Action beforePresent;
                if (!BeforePresentQueue.TryDequeue(out beforePresent))
                    continue;

                beforePresent();
            }

            BeforePresentStopwatch.Stop();
        }

        protected void RunAfterPresentHandlers () {
            while (AfterPresentQueue.Count > 0) {
                Action afterPresent;
                if (!AfterPresentQueue.TryDequeue(out afterPresent))
                    continue;

                afterPresent();
            }
        }

        public bool TryWaitForPresentToStart (int millisecondsTimeout, int delayMs = 1) {
            var now = Time.Ticks;
            var waitEnd = now + (millisecondsTimeout + delayMs) * Time.MillisecondInTicks;
            if (!PresentBegunSignal.WaitOne(millisecondsTimeout))
                return false;

            var offset = Time.MillisecondInTicks * delayMs;
            var expected = PresentBegunWhen + offset;

            now = Time.Ticks;
            if (now >= waitEnd)
                return false;
            if (now < expected)
                Thread.SpinWait(50);
            now = Time.Ticks;

            while (now < expected) {
                if (now >= waitEnd)
                    return false;
                Thread.Yield();
                now = Time.Ticks;
            }

            return true;
        }

        private void SetPresentBegun () {
            var now = Time.Ticks;
            PresentBegunWhen = now;
            PresentBegunSignal.Set();
        }

        protected void RenderFrameToDraw (Frame frameToDraw, bool endDraw) {
            try {
                PresentBegunSignal.Reset();

                if (frameToDraw != null) {
                    Manager.PrepareManager.AssertEmpty();
                    Manager.FlushBufferGenerators(frameToDraw.Index);
                    RenderFrame(frameToDraw, true);
                }

                if (endDraw) {
                    RunBeforePresentHandlers();
                    SetPresentBegun();
                    _SyncEndDraw();
                }

                FlushPendingDisposes();

                if (endDraw)
                    RunAfterPresentHandlers();
            } finally {
                if (frameToDraw != null)
                    frameToDraw.Dispose();
            }
        }

        protected void ThreadedDraw (Frame frame) {
            try {
                if (!_Running)
                    return;

                CheckMainThread(DoThreadedIssue);

                lock (DrawLock)
                    RenderFrameToDraw(frame, true);

                _DeviceLost |= IsDeviceLost;
            } catch (InvalidOperationException ioe) {
                // XNA generates this on exit and we can't do anything about it
                if (ioe.Message == "An unexpected error has occurred.") {
                    ;
                } else if (ioe is ObjectDisposedException) {
                    if (Device.IsDisposed)
                        _Running = false;
                    else
                        throw;
                } else {
                    throw;
                }
            } catch (DeviceLostException) {
                _DeviceLost = true;
            }
        }

        protected bool IsDeviceLost {
            get {
                var device = Device;
                if (device == null)
                    return false;

                return device.GraphicsDeviceStatus != GraphicsDeviceStatus.Normal;
            }
        }

        /// <summary>
        /// Synchronously renders a complete frame to the specified render target.
        /// Automatically sets up the device's viewport and the view transform of your materials and restores them afterwards.
        /// </summary>
        public bool SynchronousDrawToRenderTarget (RenderTarget2D renderTarget, DefaultMaterialSet materials, Action<Frame> drawBehavior) {
            if (renderTarget.IsDisposed)
                return false;
            if (!SynchronousDrawsEnabled)
                throw new InvalidOperationException("Synchronous draws not available inside of Game.Draw");

            WaitForActiveDraw();

            var oldDrawIsActive = Interlocked.Exchange(ref _SynchronousDrawIsActive, 1);
            if (oldDrawIsActive != 0)
                throw new InvalidOperationException("A synchronous draw is already in progress");

            _SynchronousDrawFinishedSignal.Reset();

            WaitForActiveDraw();

            var oldLazyState = materials.LazyViewTransformChanges;
            try {
                materials.LazyViewTransformChanges = false;
                materials.ApplyViewTransform(materials.ViewTransform, true);
                using (var frame = Manager.CreateFrame()) {
                    frame.ChangeRenderTargets = false;
                    frame.Label = "Synchronous Draw";
                    materials.PushViewTransform(ViewTransform.CreateOrthographic(renderTarget.Width, renderTarget.Height));

                    drawBehavior(frame);

                    PrepareNextFrame(frame, false);

                    var oldRenderTargets = Device.GetRenderTargets();
                    var oldViewport = Device.Viewport;
                    try {
                        Device.SetRenderTarget(renderTarget);
                        RenderManager.ResetDeviceState(Device);
                        Device.Viewport = new Viewport(0, 0, renderTarget.Width, renderTarget.Height);
                        Device.Clear(Color.Transparent);

                        RenderFrameToDraw(frame, false);
                    } finally {
                        Device.SetRenderTargets(oldRenderTargets);
                        materials.PopViewTransform();
                        Device.Viewport = oldViewport;
                    }
                }

                return true;
            } finally {
                materials.LazyViewTransformChanges = oldLazyState;
                _SynchronousDrawFinishedSignal.Set();
                Interlocked.Exchange(ref _SynchronousDrawIsActive, 0);
            }
        }

        public GraphicsDevice Device {
            get {
                return Manager.DeviceManager.Device;
            }
        }

        public Frame Frame {
            get {
                if (_Running) {
                    var f = _FrameBeingPrepared;
                    if (f != null)
                        return f;
                    else
                        throw new InvalidOperationException("Not preparing a frame");
                } else
                    throw new InvalidOperationException("Not running");
            }
        }

        public bool TryGetPreparingFrame (out Frame frame) {
            frame = null;

            if (!_Running)
                return false;

            frame = _FrameBeingPrepared;
            return (frame != null);
        }

        public void Dispose () {
            if (!_Running) {
                FlushPendingDisposes();
                return;
            }

            if (IsDisposed)
                return;

            // HACK
            Manager.PrepareManager.Group.Dispose();

            _Running = false;
            IsDisposed = true;

            try {
                WaitForActiveDraws();

                FlushPendingDisposes();
            } catch (ObjectDisposedException) {
            } catch (DeviceLostException) {
            } catch (DeviceNotResetException) {
            }
        }

        // TODO: Move this
        internal static void FlushDisposeList (List<IDisposable> list) {
            IDisposable[] pds = null;

            lock (list) {
                if (list.Count == 0)
                    return;

                // Prevents a deadlock from recursion
                pds = list.ToArray();
                list.Clear();
            }

            foreach (var pd in pds) {
                try {
                    pd.Dispose();
                } catch (ObjectDisposedException) {
                }
            }
        }

        private void FlushPendingDisposes () {
            lock (CreateResourceLock)
            lock (UseResourceLock) {
                FlushDisposeList(_PendingDisposes);

                Manager.FlushPendingDisposes();
            }
        }

        public void DisposeResource (IDisposable resource) {
            if (resource == null)
                return;

            if (IsDisposed) {
                resource.Dispose();
                return;
            }

            lock (_PendingDisposes)
                _PendingDisposes.Add(resource);
        }
    }
}
