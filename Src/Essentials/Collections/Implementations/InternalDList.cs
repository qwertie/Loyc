﻿/*
 * Created by SharpDevelop.
 * User: Pook
 * Date: 4/12/2011
 * Time: 9:15 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
namespace Loyc.Collections.Impl
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using Loyc.Essentials;

	/// <summary>A compact auto-enlarging deque structure that is intended to be 
	/// used within other data structures. It should only be used internally in
	/// "private" or "protected" members of low-level code. In most cases, you
	/// should use <see cref="DList{T}"/> instead.
	/// </summary>
	/// <remarks>
	/// InternalDeque is a struct, not a class, in order to save memory; and for 
	/// maximum performance, it asserts rather than throwing an exception 
	/// when an incorrect array index is used (the one exception is the iterator,
	/// which throws in case the collection is modified during enumeration; this 
	/// is for the sake of <see cref="DList{T}"/>.) For these and other reasons, one
	/// should not expose it in a public API, and it should only be used when 
	/// performance trumps all other concerns.
	/// <para/>
	/// Also, do not use the default contructor. Always specify an initial 
	/// capacity or copy InternalDeque.Empty so that the internal array gets a 
	/// value. All methods in this structure assume _array is not null.
	/// <para/>
	/// </remarks>
	[Serializable()]
	[DebuggerTypeProxy(typeof(ListSourceDebugView<>)), DebuggerDisplay("Count = {Count}")]
	public struct InternalDList<T> : IListEx<T>, IDeque<T>, ICloneable<InternalDList<T>>
	{
		public static readonly T[] EmptyArray = InternalList<T>.EmptyArray;
		public static readonly InternalDList<T> Empty = new InternalDList<T>(0);
		internal T[] _array;
		internal int _count, _start;

		public InternalDList(int capacity)
		{
			_count = _start = 0;
			_array = capacity != 0 ? new T[capacity] : EmptyArray;
		}

		private int FirstHalfSize { get { return Math.Min(_array.Length - _start, _count); } }

		public T[] InternalArray { get { return _array; } }

		public int Internalize(int index)
		{
			Debug.Assert((uint)index <= (uint)_count);
			return InternalizeNC(index);
		}
		private int InternalizeNC(int index)
		{
			index += _start;
			if (index - _array.Length >= 0)
				return index - _array.Length;
			return index;
		}

		public int IncMod(int index)
		{
			if (++index == _array.Length)
				index -= _array.Length;
			return index;
		}
		public int IncMod(int index, int amount)
		{
			if ((index += amount) >= _array.Length)
				index -= _array.Length;
			return index;
		}
		public int DecMod(int index)
		{
			if (index == 0)
				return _array.Length - 1;
			return index - 1;
		}
		public int DecMod(int index, int amount)
		{
			if ((index -= amount) < 0)
				index += _array.Length;
			return index;
		}

		public int IndexOf(T item)
		{
			EqualityComparer<T> comparer = EqualityComparer<T>.Default;
			int size1 = FirstHalfSize;
			int stop = _start + size1;
			int stop2 = _count - size1;
			int returnAdjustment = -_start;
			
			for (int i = _start;;) {
				for (; i < stop; i++) {
					if (comparer.Equals(item, _array[i]))
						return i + returnAdjustment;
				}
				if (stop == stop2)
					return -1;
				stop = stop2;
				returnAdjustment = size1;
				i = 0;
			}
		}

		public void PushLast(ICollection<T> items)
		{
			AutoEnlarge(items.Count);
			PushLast((IEnumerable<T>)items);
		}
		public void PushLast(IEnumerable<T> items)
		{
			foreach(T item in items)
				PushLast(item);
		}
		public void PushLast(ISource<T> items)
		{
			AutoEnlarge(items.Count);
			PushLast((IIterable<T>)items);
		}
		public void PushLast(IIterable<T> items)
		{
			for (Iterator<T> it = items.GetIterator();;)
			{
				bool ended = false;
				T item = it(ref ended);
				if (ended) break;
				PushLast(item);
			}
		}

		public void PushLast(T item)
		{
			AutoEnlarge(1);
			
			int i = _start + _count;
			if (i >= _array.Length)
				i -= _array.Length;
			_array[i] = item;
			++_count;
		}
		
		public void PushFirst(T item)
		{
			AutoEnlarge(1);

			if (--_start < 0)
				_start += _array.Length;
			_array[_start] = item;
		}

		public void PopLast(int amount)
		{
			if (amount == 0)
				return;
			Debug.Assert((uint)amount < (uint)_count);
			
			_count -= amount;
			int i = IncMod(_start, _count);
			for (;;) {
				_array[i] = default(T);
				if (--amount == 0)
					break;
				i = IncMod(i);
			}

			AutoShrink();
		}

		public void PopFirst(int amount)
		{
			Debug.Assert(amount <= _count);
			
			int i = _start;
			_start = IncMod(_start, amount);
			while (i != _start) {
				_array[i] = default(T);
				i = IncMod(i);
			}

			AutoShrink();
		}

		private void AutoShrink()
		{
 			if ((_count << 1) + 2 < _array.Length)
				Capacity = _count + 2;
		}
		public void AutoEnlarge(int more)
		{
			if (_count + more > _array.Length)
				Capacity = InternalList.NextLargerSize(_count + more - 1);
		}
		public void AutoEnlarge(int more, int capacityLimit)
		{
			if (_count + more > _array.Length)
				Capacity = InternalList.NextLargerSize(_count + more - 1, capacityLimit);
		}

		public int Capacity
		{
			get { return _array.Length; }
			set {
				int delta = value - _array.Length;
				if (delta == 0)
					return;

				Debug.Assert(value >= _count);

				T[] newArray = new T[value];
				
				int size1 = FirstHalfSize;
				int size2 = _count - size1;
				Array.Copy(_array, _start, newArray, 0, size1);
				if (size2 > 0)
				    Array.Copy(_array, 0, newArray, size1, size2);
				
				_start = 0;
				_array = newArray;
			}
		}

		public void Insert(int index, T item)
		{
			InsertHelper(index, 1);
			_array[Internalize(index)] = item;
		}

		public void InsertRange(int index, ICollection<T> items)
		{
			// Note: this is written so that the invariants hold if the
			// collection throws or returns an incorrect Count.
			int amount = items.Count;
			int iindex = InsertHelper(index, amount);
			var it = items.GetEnumerator();
			for (int copied = 0; copied < amount; copied++)
			{
				if (!it.MoveNext())
					break;
				_array[iindex] = it.Current;
				iindex = IncMod(iindex);
			}
		}

		public void InsertRange(int index, ISource<T> items)
		{
			// Note: this is written so that the invariants hold if the
			// collection throws or returns an incorrect Count.
			int amount = items.Count;
			int iindex = InsertHelper(index, amount);
			var it = items.GetIterator();
			for (int copied = 0; copied < amount; copied++)
			{
				if (!it.MoveNext(out _array[iindex]))
					break;
				iindex = IncMod(iindex);
			}
		}

		private int InsertHelper(int index, int amount)
		{
			Debug.Assert((uint)index <= (uint)_count);
			
			AutoEnlarge(amount);

			int deltaB = _count - index;
			if (index < deltaB)
			{
				_count += amount;
				return IH_InsertFront(index, amount);
			}
			else
			{
				if (index >= _count)
				{
					_count += amount;
					return InternalizeNC(index);
				}
				_count += amount;
				return IH_InsertBack(index, amount);
			}
		}

		private int IH_InsertFront(int index, int amount)
		{
			// Insert into front half. For example:
			// _array[20] before:    [e f g h i j k l m n o p _ _ _ _ a b c d]
			// (_count=16)                  ^index=7, amount=2        ^_start=16
			//                                                 iTo^   ^iFrom
			// after first loop:     [e f g h i j k l m n o p _ _ A B C D E F]
			// after second loop:    [G f g h i j k l m n o p _ _ A B C D E F]
			// Space for new elems:  [G * * h i j k l m n o p _ _ A B C D E F]
			// (_count=18)              ^return value=1     ^_start=14
			int start = _start = DecMod(_start, amount);
			if (index <= 0)
				return _start;
			
			T[] array = _array;
			int iFrom = InternalizeNC(amount);
			int iTo = InternalizeNC(0);
			int end = iTo + index, end2 = 0;
			if ((uint)end >= (uint)array.Length)
			{
				end2 = end - array.Length;
				end = array.Length;
			}
			for (; (uint)iTo < (uint)end; iTo++)
			{
				array[iTo] = array[iFrom];
				if (++iFrom >= array.Length)
					iFrom = 0;
			}
			for (iTo = 0; iTo < end2; iTo++)
			{
				array[iTo] = array[iFrom];
				if (++iFrom >= array.Length)
					iFrom = 0;
			}
			return end2;
		}
		private int IH_InsertBack(int index, int amount)
		{
			// Insert into back half. For example:
			// _array[20]:           [m n o p _ _ _ _ a b c d e f g h i j k l]
			// (_count=16)             iFrom^   ^iTo  ^_start=8         ^index=9, amount=2
			// after first loop:     [K L M N O P _ _ a b c d e f g h i j k l]
			// after second loop:    [K L M N O P _ _ a b c d e f g h i j k J]
			// Space for new elems:  [K L M N O P _ _ a b c d e f g h i * * J]
			// (_count=14)                            ^_start=8         ^return value

			// Note: '_count' has already been increased by 'amount'
			T[] array = _array;
			int iFrom = InternalizeNC(_count - 1);
			int iTo = InternalizeNC(_count - amount - 1);
			int left = _count - amount - index; // number of elements to move
			int iStop;
			if (iTo < _start) {
				iStop = -1;
				if (iTo >= left)
					iStop = iTo - left;
				left -= iTo + 1;

				for (; iTo > iStop; iTo--)
				{
					array[iTo] = array[iFrom];
					if (--iFrom < 0)
						iFrom += array.Length;
				}
				iTo = array.Length - 1;
			}

			for (iStop = iTo - left; iTo > iStop; iTo--)
			{
				array[iTo] = array[iFrom];
				if (--iFrom < 0)
					iFrom += array.Length;
			}
			return iFrom;
		}

		public void RemoveAt(int index)
		{
			RemoveHelper(index, 1);
		}
		public void RemoveRange(int index, int amount)
		{
			Debug.Assert (amount >= 0);

			RemoveHelper(index, amount);
		}

		private static void CopyFwd(T[] array, int from, int to, int amount)
		{
			Debug.Assert(Math.Min(from, to) >= 0 && amount >= 0);
			Debug.Assert(Math.Max(from, to) + amount <= array.Length);
			Debug.Assert(to < from || to >= from + amount);
			int stop = to + amount;
			while (to < stop)
				array[to++] = array[from++];
		}
		private static void CopyBwd(T[] array, int fromPlusAmount, int toPlusAmount, int amount)
		{
			Debug.Assert(Math.Min(fromPlusAmount, toPlusAmount) >= amount && amount >= 0);
			Debug.Assert(Math.Max(fromPlusAmount, toPlusAmount) <= array.Length);
			Debug.Assert(toPlusAmount > fromPlusAmount || toPlusAmount <= fromPlusAmount-amount);
			int to = toPlusAmount - amount;
			while (toPlusAmount > to)
				array[--toPlusAmount] = array[--fromPlusAmount];
		}

		private void RemoveHelper(int index, int amount)
		{
			Debug.Assert((uint)index <= (uint)_count && (uint)(index + amount) <= (uint)_count);
			T[] array = _array;
			int start = _start;

			int deltaB = _count - index;
			if (index < deltaB)
			{
				if (index > 0)
					RH_CollapseFront(index, amount);

				// Clear deleted elements (to be GC-friendly)
				RH_Clear(ref _start, amount);
			}
			else
			{
				if (index < _count)
					RH_CollapseBack(index, amount);
				
				// Clear deleted elements (to be GC-friendly)
				int clearIndex = Internalize(_count);
				RH_Clear(ref clearIndex, amount);
			}
			_count -= amount;
			AutoShrink();
		}

		private void RH_Clear(ref int start, int amount)
		{
			T[] array = _array;				
			int adjusted = start + amount;
			if (adjusted >= array.Length) {
				adjusted -= array.Length;
				for (int i = start; i < array.Length; i++)
					array[i] = default(T);
				start = 0;
			}
			for (int i = start; i < adjusted; i++)
				array[i] = default(T);
			start = adjusted;
		}

		private void RH_CollapseFront(int index, int amount)
		{
			T[] array = _array;
			int start = _start;
			
			// Collapse front half. For example:
			// _array[20]:              [e f g h i j k l m n o p _ _ _ _ a b c d]
			// (_count=16)                     ^index=7, amount=2        ^_start=16
			//                         from-1^   ^to-1
			// CopyBwd within 2nd half: [e f E F G j k l m n o p _ _ _ _ a b c d]
			//                             ^to-1                         from-1^
			// CopyFwd 1st->2nd half:   [C D E F G j k l m n o p _ _ _ _ a b c d]
			//                                                       from-1^   ^to-1
			// copyFwd within 1st half: [C D E F G j k l m n o p _ _ _ _ a b A B]
			// Clear deleted elems:     [C D E F i j k l m n o p _ _ _ _ _ _ A B]
			// (_count=14)                                                   ^_start=18
			int from = start + index;
			int to = from + amount;
			if (to > array.Length) {
				to -= array.Length;
				if (from > array.Length) {
					from -= array.Length;
					CopyBwd(array, from, to, from);
					to -= from;
					from = array.Length;
				}
				if (to < from - start) {
					CopyBwd(array, from, to, to);
					from -= to;
					to = array.Length;
				}
			}
			CopyBwd(array, from, to, from - start);
			_start = start + amount;
		}
		private void RH_CollapseBack(int index, int amount)
		{
			T[] array = _array;
			int start = _start;

			// Collapse back half. For example:
			// _array[20]:           [m n o p _ _ _ _ a b c d e f g h i j k l]
			// (_count=16)                            ^_start=8         ^index=9, amount=2
			// Copy within 1st half: [m n o p _ _ _ _ a b c d e f g h i L k l]
			//                                                        to^   ^from 
			// Copy 2nd->1st half:   [m n o p _ _ _ _ a b c d e f g h i L M N]
			//                        ^from                               ^to
			// Copy within 2nd half: [O P o p _ _ _ _ a b c d e f g h i L M N]
			//                        ^to ^from
			// Clear deleted elems:  [O P _ _ _ _ _ _ a b c d e f g h K L M N]
			// (_count=14)                                                ^_start=18

			int to = start + index;
			int from = to + amount;
			int left = _count - index - amount;
			int copyAmt;
			if (from + left > array.Length) {
				CopyFwd(array, from, to, copyAmt = array.Length - from);
				from = 0;
				to += copyAmt;
				left -= copyAmt;
				if (to + left > array.Length) {
					CopyFwd(array, from, to, copyAmt = array.Length - to);
					from += copyAmt;
					to = 0;
					left -= copyAmt;
				}
			}
			CopyFwd(array, from, to, left);
		}

		public T this[int index]
		{
			[DebuggerStepThrough]
			get {
				Debug.Assert((uint)index < (uint)_count);
				return _array[Internalize(index)];
			}
			[DebuggerStepThrough]
			set {
				Debug.Assert((uint)index < (uint)_count);
				_array[Internalize(index)] = value;
			}
		}

		public bool TrySet(int index, T value)
		{
			if ((uint)index >= (uint)_count)
				return false;
			_array[Internalize(index)] = value;
			return true;
		}
		public T TryGet(int index, ref bool fail)
		{
			if ((uint)index < (uint)_count)
				return _array[Internalize(index)];
			else {
				fail = true;
				return default(T);
			}
		}

		/// <summary>An alias for PushLast().</summary>
		public void Add(T item)
		{
			PushLast(item);
		}

		public void Clear()
		{
			_array = InternalList<T>.EmptyArray;
			_count = _start = 0;
		}

		public bool Contains(T item)
		{
			return IndexOf(item) > -1;
		}

		public void CopyTo(T[] array, int arrayIndex)
		{
			Debug.Assert(array != null && array.Length >= _count);
			Debug.Assert(arrayIndex >= 0 && array.Length - arrayIndex >= _count);
			int iindex = _start;
			for (int i = 0; i < _count; i++) {
				array[i + arrayIndex] = _array[iindex];
				iindex = IncMod(iindex);
			}
		}

		public int Count
		{
			[DebuggerStepThrough]
			get { return _count; }
		}

		public bool IsReadOnly
		{
			get { return false; }
		}

		public bool Remove(T item)
		{
			int i = IndexOf(item);
			if (i <= -1)
				return false;
			RemoveAt(i);
			return true;
		}

		IEnumerator<T> IEnumerable<T>.GetEnumerator()
		{
			return GetEnumerator();
			/*int checksum = Checksum;
			int size1 = FirstHalfSize;
			int stop = _start + size1;
			int stop2 = _count - size1;
			
			for (int i = _start;;) {
				for (; i < stop; i++) {
					if (checksum != Checksum)
						throw new InvalidOperationException("The collection was modified after enumeration started.");
					yield return _array[i];
				}
				if (stop == stop2)
					yield break;
				stop = stop2;
				i = 0;
			}*/
		}
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
		public IEnumerator<T> GetEnumerator()
		{
			return GetIterator().AsEnumerator();
		}

		public Iterator<T> GetIterator()
		{
			int size1 = FirstHalfSize;
			int stop = _start + size1;
			int stop2 = _count - size1;
			int i = _start;
			// we must make a copy because the iterator could be called after the 
			// InternalDeque ceases to exist.
			T[] array = _array;
			
			return delegate(ref bool ended)
			{
				while (i >= stop) {
					if (stop == stop2) {
						ended = true;
						return default(T);
					}
					stop = stop2;
					i = 0;
				}

				return array[i++];
			};
		}

		#region IDeque<T>

		public T TryPopFirst(ref bool isEmpty)
		{
			T value = TryPeekFirst(ref isEmpty);
			if (!isEmpty)
				PopFirst(1);
			return value;
		}
		public T TryPeekFirst(ref bool isEmpty)
		{
			if (_count > 0)
				return First;
			isEmpty = true;
			return default(T);
		}
		public T TryPopLast(ref bool isEmpty)
		{
			T value = TryPeekLast(ref isEmpty);
			if (!isEmpty)
				PopLast(1);
			return value;
		}
		public T TryPeekLast(ref bool isEmpty)
		{
			if (_count > 0)
				return Last;
			isEmpty = true;
			return default(T);
		}

		public T First
		{
			get { return this[0]; }
			set { this[0] = value; }
		}
		public T Last
		{
			get { return this[_count - 1]; }
			set { this[_count - 1] = value; }
		}
		public bool IsEmpty
		{
			get { return _count <= 0; }
		}

		#endregion

		public int BinarySearch(T k, Comparer<T> comp)
		{
			int low = 0;
			int high = _count - 1;
			while (low <= high)
			{
				int mid = low + ((high - low) >> 1);
				T midk = this[mid];
				int c = comp.Compare(midk, k);
				if (c < 0)
					low = mid + 1;
				else if (c > 0)
					high = mid - 1;
				else
					return mid;
			}

			return ~low;
		}

		public int BinarySearch<K>(K k, Func<T, K, int> compare)
		{
			int low = 0;
			int high = _count - 1;
			while (low <= high)
			{
				int mid = low + ((high - low) >> 1);
				T midk = this[mid];
				int c = compare(midk, k);
				if (c < 0)
					low = mid + 1;
				else if (c > 0)
					high = mid - 1;
				else
					return mid;
			}

			return ~low;
		}

		public InternalDList<T> Clone()
		{
			InternalDList<T> clone = new InternalDList<T>();
			clone._array = new T[Count];
			CopyTo(clone._array, 0);
			clone._start = 0;
			clone._count = Count;
			return clone;
		}
	}
}
