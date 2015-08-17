﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Squared.Util.DeclarativeSort {
    public interface IHasTags {
        void GetTags (out Tags tags);
    }

    public interface IHasAttribute<TAttribute> {
        void GetAttribute (out TAttribute attribute);
    }

    public interface IValueExtractor<TValue> {
        bool GetTags                 (ref TValue value, out Tags tags);
        bool GetAttribute<TAttribute>(ref TValue value, out TAttribute attribute);
    }

    public struct Tags {
        public static readonly Tags Null = default(Tags);

        private readonly Tag Tag;
        private readonly TagSet TagSet;

        public Tags (Tag tag) {
            if (tag == null)
                throw new ArgumentNullException(nameof(tag));

            Tag = tag;
            TagSet = null;
        }

        public Tags (TagSet tagSet) {
            if (tagSet == null)
                throw new ArgumentNullException(nameof(tagSet));

            Tag = null;
            TagSet = tagSet;
        }

        public bool Contains (Tags tags) {
            if (TagSet != null)
                return TagSet.Contains(tags);
            else if (Tag != null)
                return (tags.Count == 1) && (Tag == tags[0]);
            else
                return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Contains (Tag tag) {
            return Contains((Tags)tag);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Contains (TagSet tags) {
            return Contains((Tags)tags);
        }

        public bool IsNull {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            get {
                return (Tag == null) && (TagSet == null);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Equals (Tags tags) {
            return (Tag == tags.Tag) && (TagSet == tags.TagSet);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Equals (Tag tag) {
            return (Tag == tag) && (TagSet == null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Equals (TagSet tagSet) {
            return (Tag == null) && (TagSet == tagSet);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public override bool Equals (object obj) {
            if (obj == null)
                return false;

            if (obj is Tags)
                return Equals((Tags)obj);

            var tag = obj as Tag;
            if (tag != null)
                return Equals(tag);

            var tagset = obj as TagSet;
            if (tagset != null)
                return Equals(tagset);

            return false;
        }

        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            get {
                if (TagSet != null)
                    return TagSet.Tags.Length;
                else if (Tag != null)
                    return 1;
                else
                    return 0;
            }
        }

        public Tag this [int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            get {
                if (TagSet != null)
                    return TagSet.Tags[index];
                else if (Tag != null)
                    return Tag;
                else
                    throw new IndexOutOfRangeException();
            }
        }

        internal Dictionary<Tag, Tags> TransitionCache {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            get {
                if (TagSet != null)
                    return TagSet.TransitionCache;
                else if (Tag != null)
                    return Tag.TransitionCache;
                else
                    // FIXME
                    throw new InvalidOperationException();
            }
        }

        public override int GetHashCode () {
            if (Tag != null)
                return Tag.GetHashCode();
            else if (TagSet != null)
                return TagSet.GetHashCode();
            else
                return 0;
        }

        public override string ToString () {
            if (Tag != null)
                return Tag.ToString();
            else if (TagSet != null)
                return TagSet.ToString();
            else
                return "<Null Tags>";
        }

        public static bool operator == (Tags lhs, Tags rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator != (Tags lhs, Tags rhs) {
            return !lhs.Equals(rhs);
        }

        public static bool operator == (Tags lhs, Tag rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator != (Tags lhs, Tag rhs) {
            return !lhs.Equals(rhs);
        }

        public static bool operator == (Tags lhs, TagSet rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator != (Tags lhs, TagSet rhs) {
            return !lhs.Equals(rhs);
        }

        public static bool operator == (Tag lhs, Tags rhs) {
            return rhs.Equals(lhs);
        }

        public static bool operator != (Tag lhs, Tags rhs) {
            return !rhs.Equals(lhs);
        }

        public static bool operator == (TagSet lhs, Tags rhs) {
            return rhs.Equals(lhs);
        }

        public static bool operator != (TagSet lhs, Tags rhs) {
            return !rhs.Equals(lhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags operator + (Tag lhs, Tags rhs) {
            return TagSet.Transition(rhs, lhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags operator + (Tags lhs, Tag rhs) {
            return TagSet.Transition(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags operator + (Tags lhs, Tags rhs) {
            return TagSet.New(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator < (Tags lhs, Tags rhs) {
            return new TagOrdering(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator > (Tags lhs, Tags rhs) {
            return new TagOrdering(rhs, lhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator < (Tags lhs, Tag rhs) {
            return new TagOrdering(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator > (Tags lhs, Tag rhs) {
            return new TagOrdering(rhs, lhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator < (Tag lhs, Tags rhs) {
            return new TagOrdering(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator > (Tag lhs, Tags rhs) {
            return new TagOrdering(rhs, lhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static implicit operator Tags (Tag tag) {
            return new Tags(tag);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static implicit operator Tags (TagSet tagSet) {
            return new Tags(tagSet);
        }

        public object Object {
            get {
                return (object)Tag ?? (object)TagSet;
            }
        }
    }

    public class Tag {
        public class EqualityComparer : IEqualityComparer<Tag> {
            public static readonly EqualityComparer Instance = new EqualityComparer();

            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            public bool Equals (Tag x, Tag y) {
                return ReferenceEquals(x, y);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            public int GetHashCode (Tag tag) {
                return tag.Id;
            }
        }

        // Only for sorting within tag arrays to make equality comparisons of tag arrays valid
        internal class Comparer : IComparer<Tag> {
            public static readonly Comparer Instance = new Comparer();

            public int Compare (Tag x, Tag y) {
                return y.Id - x.Id;
            }
        }

        private static int NextId = 1;
        private static readonly Dictionary<string, Tag> TagCache = new Dictionary<string, Tag>();

        internal readonly Dictionary<Tag, Tags> TransitionCache = new Dictionary<Tag, Tags>(EqualityComparer.Instance);

        public readonly string Name;
        public readonly int    Id;

        internal Tag (string name) {
            Name = name;
            Id = NextId++;
        }

        public override int GetHashCode () {
            return Id;
        }

        public override bool Equals (object obj) {
            if (obj is Tags)
                return ReferenceEquals(this, ((Tags)obj).Object);
            else
                return ReferenceEquals(this, obj);
        }

        public override string ToString () {
            return Name;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags operator + (Tag lhs, Tag rhs) {
            return new Tags(lhs) + rhs;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator < (Tag lhs, Tag rhs) {
            return new TagOrdering(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator > (Tag lhs, Tag rhs) {
            return new TagOrdering(rhs, lhs);
        }

        /// <returns>Whether lhs contains rhs.</returns>
        public static bool operator & (Tags lhs, Tag rhs) {
            return lhs.Contains(rhs);
        }

        /// <returns>Whether lhs does not contain rhs.</returns>
        public static bool operator ^ (Tags lhs, Tag rhs) {
            return !lhs.Contains(rhs);
        }

        public static Tag New (string name) {
            Tag result;

            lock (TagCache)
            if (!TagCache.TryGetValue(name, out result))
                TagCache.Add(name, result = new Tag(string.Intern(name)));

            return result;
        }

        /// <summary>
        /// Finds all static Tag fields of type and ensures they are initialized.
        /// If instance is provided, also initializes all non-static Tag fields of that instance.
        /// </summary>
        public static void AutoCreate (Type type, object instance = null) {
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            if (instance != null)
                flags |= BindingFlags.Instance;

            var tTag = typeof(Tag);

            lock (type)
            foreach (var f in type.GetFields(flags)) {
                if (f.FieldType != tTag)
                    continue;

                object lookupInstance = null;
                if (!f.IsStatic)
                    lookupInstance = instance;

                var tag = f.GetValue(lookupInstance);
                if (tag == null)
                    f.SetValue(lookupInstance, New(f.Name));
            }
        }

        /// <summary>
        /// Finds all static Tag fields of type and ensures they are initialized.
        /// If instance is provided, also initializes all non-static Tag fields of that instance.
        /// </summary>
        public static void AutoCreate<T> (T instance = default(T)) {
            AutoCreate(typeof(T), instance);
        }
    }

    public partial class TagSet : IEnumerable<Tag> {
        private static int NextId = 1;

        private string _CachedToString;
        private readonly HashSet<Tag> HashSet = new HashSet<Tag>();
        internal readonly Tag[] Tags;
        internal Dictionary<Tag, Tags> TransitionCache { get; private set; }
        public readonly int Id;

        private TagSet (Tag[] tags) {
            if (tags == null)
                throw new ArgumentNullException(nameof(tags));
            if (tags.Length == 0)
                throw new ArgumentOutOfRangeException(nameof(tags), "Must not be empty");

            Tags = (Tag[]) tags.Clone();
            foreach (var tag in tags)
                HashSet.Add(tag);

            TransitionCache = new Dictionary<Tag, Tags>(Tag.EqualityComparer.Instance);
            Id = NextId++;
        }

        /// <returns>Whether this tagset contains all the tags in rhs.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Contains (Tags rhs) {
            if (rhs == this)
                return true;

            for (int l = rhs.Count, i = 0; i < l; i++) {
                if (!HashSet.Contains(rhs[i]))
                    return false;
            }

            return true;
        }

        public override int GetHashCode () {
            return Id;
        }

        public override bool Equals (object obj) {
            if (obj is Tags)
                return ReferenceEquals(this, ((Tags)obj).Object);
            else
                return ReferenceEquals(this, obj);
        }

        public override string ToString () {
            if (_CachedToString != null)
                return _CachedToString;

            return _CachedToString = string.Format("<{0}>", string.Join<Tag>(", ", Tags.OrderBy(t => t.ToString())));
        }

        IEnumerator<Tag> IEnumerable<Tag>.GetEnumerator () {
            return ((IEnumerable<Tag>)Tags).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return Tags.GetEnumerator();
        }
    }

    public partial class TagSet {
        private class TagArrayComparer : IEqualityComparer<Tag[]> {
            public bool Equals (Tag[] x, Tag[] y) {
                return x.SequenceEqual(y);
            }

            public int GetHashCode (Tag[] tags) {
                var result = 0;
                foreach (var tag in tags)
                    result = (result << 2) ^ tag.Id;
                return result;
            }
        }

        internal static readonly Dictionary<Tag[], TagSet> SetCache = new Dictionary<Tag[], TagSet>(new TagArrayComparer());

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        internal static Tags Transition (Tags lhs, Tag rhs) {
            Tags result;

            if (rhs == lhs)
                return lhs;

            bool existing;
            lock (lhs.TransitionCache)
                existing = lhs.TransitionCache.TryGetValue(rhs, out result);

            if (existing)
                return result;
            else
                return TransitionSlow(lhs, rhs);
        }

        internal static Tags TransitionSlow (Tags lhs, Tag rhs) {
            var newTags = new Tag[lhs.Count + 1];

            for (var i = 0; i < newTags.Length - 1; i++) {
                var tag = lhs[i];
                if (tag == rhs)
                    return lhs;

                newTags[i] = tag;
            }

            newTags[newTags.Length - 1] = rhs;
                
            Array.Sort(newTags, Tag.Comparer.Instance);

            var result = New(newTags);
                
            lock (lhs.TransitionCache) {
                if (!lhs.TransitionCache.ContainsKey(rhs))
                    lhs.TransitionCache.Add(rhs, result);
            }

            return result;
        }

        internal static TagSet New (Tag[] tags) {
            TagSet result;

            lock (SetCache)
            if (!SetCache.TryGetValue(tags, out result))
                SetCache.Add(tags, result = new TagSet(tags));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags New (Tags lhs, Tags rhs) {
            if (lhs == rhs)
                return lhs;

            var lhsCount = lhs.Count;
            var rhsCount = rhs.Count;

            Tags result = lhs[0];

            for (int i = 1, l = lhs.Count; i < l; i++)
                result = Transition(result, lhs[i]);

            for (int i = 0, l = rhs.Count; i < l; i++)
                result = Transition(result, rhs[i]);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags operator + (TagSet lhs, Tags rhs) {
            return New(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags operator + (Tags lhs, TagSet rhs) {
            return New(lhs, rhs);
        }
    }

    public struct TagOrdering {
        public  readonly Tags Lower, Higher;
        private readonly int   HashCode;

        public TagOrdering (Tags lower, Tags higher) {
            if (lower.IsNull)
                throw new ArgumentNullException(nameof(lower));
            else if (higher.IsNull)
                throw new ArgumentNullException(nameof(higher));

            Lower = lower;
            Higher = higher;

            HashCode = Lower.GetHashCode() ^ (Higher.GetHashCode() << 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public int Compare (Tags lhs, Tags rhs) {
            var lhsContainsLower  = lhs.Contains(Lower);
            var rhsContainsLower  = rhs.Contains(Lower);
            if (lhsContainsLower && rhsContainsLower)
                return 0;

            var lhsContainsHigher = lhs.Contains(Higher);
            var rhsContainsHigher = rhs.Contains(Higher);
            if (lhsContainsHigher && rhsContainsHigher)
                return 0;

            if (lhsContainsLower && rhsContainsHigher)
                return -1;

            if (lhsContainsHigher && rhsContainsLower)
                return 1;

            return 0;
        }

        public override int GetHashCode () {
            return HashCode;
        }

        public bool Equals (TagOrdering rhs) {
            return (Lower == rhs.Lower) && (Higher == rhs.Higher);
        }

        public override bool Equals (object rhs) {
            if (rhs is TagOrdering)
                return Equals((TagOrdering)rhs);
            else
                return false;
        }

        public override string ToString () {
            return string.Format("{0} < {1}", Lower, Higher);
        }
    }

    public class Group {
        public readonly string Name;
    }

    public class ContradictoryOrderingException : Exception {
        public readonly TagOrdering A, B;
        public readonly Tags Left, Right;

        public ContradictoryOrderingException (TagOrdering a, TagOrdering b, Tags lhs, Tags rhs) 
            : base(
                  string.Format("Orderings {0} and {1} are contradictory for {2}, {3}", a, b, lhs, rhs)
            ) {
            A = a;
            B = b;
            Left = lhs;
            Right = rhs;
        }
    }

    public class TagOrderingCollection : List<TagOrdering> {
        public int? Compare (Tags lhs, Tags rhs, out Exception error) {
            int result = 0;
            var lastOrdering = default(TagOrdering);

            foreach (var ordering in this) {
                var subResult = ordering.Compare(lhs, rhs);

                if (subResult == 0)
                    continue;
                else if (
                    (result != 0) &&
                    (Math.Sign(subResult) != Math.Sign(result))
                ) {
                    error = new ContradictoryOrderingException(
                        lastOrdering, ordering, lhs, rhs
                    );
                    return null;
                } else {
                    result = subResult;
                    lastOrdering = ordering;
                }
            }

            error = null;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public int Compare (Tags lhs, Tags rhs) {
            Exception error;
            var result = Compare(lhs, rhs, out error);

            if (result.HasValue)
                return result.Value;
            else
                throw error;
        }
    }

    public class NullValueExtractor<TValue> : IValueExtractor<TValue> {
        public bool GetAttribute<TAttribute>(ref TValue value, out TAttribute attribute) {
            attribute = default(TAttribute);
            return false;
        }

        public bool GetTags (ref TValue value, out Tags tags) {
            tags = default(Tags);
            return false;
        }
    }

    public class GenericValueExtractor<TValue> : IValueExtractor<TValue> {
        delegate bool TagGetter                  (ref TValue value, out Tags tags);
        delegate bool AttributeGetter<TAttribute>(ref TValue value, out TAttribute attribute);

        private readonly TagGetter                  _GetTags;
        private readonly Dictionary<Type, Delegate> _GetAttributeTable = new Dictionary<Type, Delegate>();

        public GenericValueExtractor () {
            var t        = typeof(TValue);
            var tHasTags = typeof(IHasTags);
            var flags    = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            if (tHasTags.IsAssignableFrom(t)) {
                var pValue = Expression.Parameter(t.MakeByRefType(), "value");
                var pTags  = Expression.Parameter(typeof(Tags).MakeByRefType(), "tags");

                var tagGetter = Expression.Lambda<TagGetter>(
                    Expression.Block(
                        Expression.Assign(pTags, Expression.Default(typeof(Tags))),
                        Expression.Call(pValue, t.GetMethod("GetTags", flags), pTags),
                        Expression.Constant(true, typeof(bool))
                    ),
                    pValue, pTags
                );
                _GetTags = tagGetter.Compile();
            } else {
                _GetTags = _NullGetTags;
            }
        }

        private static bool _NullGetTags (ref TValue value, out Tags tags) {
            tags = default(Tags);
            return false;
        }

        public bool GetAttribute<TAttribute>(ref TValue value, out TAttribute attribute) {
            attribute = default(TAttribute);
            return false;
        }

        public bool GetTags (ref TValue value, out Tags tags) {
            return _GetTags(ref value, out tags);
        }
    }

    public static class ValueExtractor<TValue> {
        public static IValueExtractor<TValue> Default {
            get;
            private set;
        }

        static ValueExtractor () {
            // FIXME: Less-than-optimal performance
            Default = new GenericValueExtractor<TValue>();
        }
    }

    public class Sorter : IEnumerable<TagOrdering> {
        private class ValueComparer<TValue> : IComparer<TValue> {
            public readonly Sorter                  Sorter;
            public readonly IValueExtractor<TValue> Extractor;
            public readonly bool                    Ascending;

            public ValueComparer (Sorter sorter, IValueExtractor<TValue> extractor, bool ascending) {
                Sorter = sorter;
                Extractor = extractor;
                Ascending = ascending;
            }

            public int Compare (TValue lhs, TValue rhs) {
                Tags lhsTags = default(Tags), rhsTags = default(Tags);

                var tagsValid = Extractor.GetTags(ref lhs, out lhsTags) &&
                                Extractor.GetTags(ref rhs, out rhsTags);

                if (!tagsValid)
                    return 0;

                var result = Sorter.Orderings.Compare(lhsTags, rhsTags);
                return (Ascending)
                    ? result
                    : -result;
            }
        }

        public readonly TagOrderingCollection Orderings = new TagOrderingCollection();

        public void Add (TagOrdering ordering) {
            Orderings.Add(ordering);
        }

        public void Add (params TagOrdering[] orderings) {
            foreach (var o in orderings)
                Orderings.Add(o);
        }

        public void Sort<TValue> (TValue[] values, bool ascending = true) {
            Sort(values, ValueExtractor<TValue>.Default, ascending: ascending);
        }

        public void Sort<TValue> (ArraySegment<TValue> values, bool ascending = true) {
            Sort(values, ValueExtractor<TValue>.Default, ascending: ascending);
        }

        public void Sort<TValue> (TValue[] values, IValueExtractor<TValue> extractor, bool ascending = true) {
            Sort(new ArraySegment<TValue>(values), extractor, ascending: ascending);
        }

        public void Sort<TValue> (ArraySegment<TValue> values, IValueExtractor<TValue> extractor, bool ascending = true) {
            Array.Sort(
                values.Array, values.Offset, values.Count,
                // Heap allocation :-(
                new ValueComparer<TValue>(this, extractor, ascending)
            );
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return Orderings.GetEnumerator();
        }

        IEnumerator<TagOrdering> IEnumerable<TagOrdering>.GetEnumerator () {
            return Orderings.GetEnumerator();
        }
    }
}
