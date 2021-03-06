using System;
using System.Diagnostics;
using System.Threading;

namespace Loyc.Threading
{
	/// <summary>
	/// Designed to be used in a "using" statement to temporarily alter a 
	/// <see cref="ThreadLocalVariable{T}"/> or <see cref="Holder{T}"/>
	/// or something else implementing <see cref="IMValue{T}"/>.
	/// </summary>
	public struct SavedValue<T> : IDisposable
	{
		readonly T _oldValue;
		readonly IMValue<T> _valueHolder;

		public SavedValue(IMValue<T> oldValue, T newValue)
		{
			_valueHolder = oldValue;
			_oldValue = oldValue.Value;
			oldValue.Value = newValue;
		}
		public void Dispose()
		{
			_valueHolder.Value = _oldValue;
		}
		
		public T OldValue { get { return _oldValue; } }
	}
}
