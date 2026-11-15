// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Buffers
{
    /// <summary>
    /// The average of the last N values pushed (or of every value, until N have been pushed)
    /// </summary>
    internal sealed class MovingAverage
    {
        private readonly long[] _values;
        private int _index;
        private int _count;
        private long _sum; // A long sum stays exact, where a running double sum would drift


        public MovingAverage(int windowSize)
        {
            if (windowSize < 1) throw new ArgumentOutOfRangeException(nameof(windowSize), "Value must be positive.");

            _values = new long[windowSize];
        }


        public double Mean => _count > 0 ? (double)_sum / _count : 0;

        public void Push(long value)
        {
            if (_count < _values.Length) _count++;
            else _sum -= _values[_index];

            _values[_index] = value;
            _sum += value;
            _index = (_index + 1) % _values.Length;
        }
    }
}
