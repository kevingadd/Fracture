﻿using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Squared.Render.STB.Native {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate int ReadCallback (void* userData, byte* data, int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void SkipCallback (void* userData, int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate int EOFCallback (void* userData);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void WriteCallback (void* userData, byte* data, int size);

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct STBI_IO_Callbacks {
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public ReadCallback read;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public SkipCallback skip;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public EOFCallback eof;
    }

    public static unsafe partial class API {
        static API () {
            try {
                var loader = new Util.EmbeddedDLLLoader(Assembly.GetExecutingAssembly());
                loader.Load(DllName + ".dll");
            } catch (Exception exc) {
                Console.Error.WriteLine("Failed to load {0}: {1}", DllName, exc.Message);
            }
        }

        const string DllName = "stb_image";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern void stbi_image_free (void* ptr);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern byte* stbi_load_from_memory (byte* buffer, int len, out int x, out int y, out int channels, int desired_channels);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern byte* stbi_load_from_callbacks (ref STBI_IO_Callbacks clbk, void *user, out int x, out int y, out int channels, int desired_channels);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern ushort* stbi_load_16_from_memory (byte* buffer, int len, out int x, out int y, out int channels, int desired_channels);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern ushort* stbi_load_16_from_callbacks (ref STBI_IO_Callbacks clbk, void *user, out int x, out int y, out int channels, int desired_channels);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern float* stbi_loadf_from_memory (byte* buffer, int len, out int x, out int y, out int channels, int desired_channels);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern float* stbi_loadf_from_callbacks (ref STBI_IO_Callbacks clbk, void *user, out int x, out int y, out int channels, int desired_channels);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_info_from_memory (byte* buffer, int len, out int x, out int y, out int comp);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_info_from_callbacks (ref STBI_IO_Callbacks clbk, void *user, out int x, out int y, out int comp);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_is_16_bit_from_memory (byte* buffer, int len);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_is_16_bit_from_callbacks (ref STBI_IO_Callbacks clbk, void *user);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern byte* stbi_failure_reason ();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_write_png_to_func (WriteCallback callback, void *user, int w, int h, int comp, byte* data, int strideInBytes);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_write_bmp_to_func (WriteCallback callback, void *user, int w, int h, int comp, byte* data);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_write_tga_to_func (WriteCallback callback, void *user, int w, int h, int comp, byte* data);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_write_jpg_to_func (WriteCallback callback, void *user, int w, int h, int comp, byte* data, int quality);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_write_hdr_to_func (WriteCallback callback, void *user, int w, int h, int comp, float* data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int get_stbi_write_png_compression_level ();
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern void set_stbi_write_png_compression_level (int level);
    }
}