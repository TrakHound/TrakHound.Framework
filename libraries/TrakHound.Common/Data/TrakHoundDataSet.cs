// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Data
{
    /// <summary>
    /// A table of values stored in a two-dimensional array, where each Row is identified by a Primary Key stored in the first Column
    /// </summary>
    public class TrakHoundDataSet<TPrimaryKey>
    {
        private readonly Dictionary<string, int> _columnIndexes = new Dictionary<string, int>();
        private readonly Dictionary<TPrimaryKey, int> _rowIndexes = new Dictionary<TPrimaryKey, int>();
        private TrakHoundDataColumn[] _columns;
        private object[,] _rows;


        /// <summary>
        /// The Columns of the DataSet (the first Column is the Primary Key)
        /// </summary>
        public TrakHoundDataColumn[] Columns => _columns;

        /// <summary>
        /// The Cell values indexed by [Row, Column] (null if no Rows have been added)
        /// </summary>
        public object[,] Rows => _rows;

        /// <summary>
        /// The number of Rows
        /// </summary>
        public int RowCount => _rows.Length / _columns.Length;

        /// <summary>
        /// The number of Columns (including the Primary Key Column)
        /// </summary>
        public int ColumnCount => _columns.Length;


        /// <summary>
        /// Creates a DataSet with a Primary Key Column using the specified ID
        /// </summary>
        public TrakHoundDataSet(string primaryKeyId)
        {
            if (!string.IsNullOrEmpty(primaryKeyId))
            {
                _columnIndexes.Add(primaryKeyId, 0);
                _columns = new TrakHoundDataColumn[1];
                _columns[0] = new TrakHoundDataColumn(0, primaryKeyId);
            }
        }


        /// <summary>
        /// Gets the Index of the Column with the specified ID (-1 if not found)
        /// </summary>
        public int GetColumnIndex(string columnId)
        {
            if (!string.IsNullOrEmpty(columnId))
            {
                if (_columnIndexes.TryGetValue(columnId, out var index))
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Gets the value of the Cell at the specified Row Index and Column ID (null if not found)
        /// </summary>
        public object GetColumnValue(int rowIndex, string columnId)
        {
            if (!string.IsNullOrEmpty(columnId) && rowIndex < RowCount)
            {
                if (_columnIndexes.TryGetValue(columnId, out var columnIndex))
                {
                    return _rows[rowIndex, columnIndex];
                }
            }

            return null;
        }

        /// <summary>
        /// Adds a Column with the specified ID and optional Name to the end of the DataSet (ignored if the ID already exists)
        /// </summary>
        public void AddColumn(string columnId, string columnName = null)
        {
            if (!string.IsNullOrEmpty(columnId))
            {
                if (_columns == null) _columns = new TrakHoundDataColumn[1];

                if (!_columnIndexes.ContainsKey(columnId))
                {
                    var columns = new TrakHoundDataColumn[_columns.Length + 1];
                    for (var i = 0; i < _columns.Length; i++) columns[i] = _columns[i];

                    var index = columns.Length - 1;
                    columns[index] = new TrakHoundDataColumn(index, columnId, columnName);

                    _columns = columns;
                    _columnIndexes.Add(columnId, index);

                    if (_rows != null)
                    {
                        _rows = ResizeArray<object>(_rows, _rows.Length, _columns.Length);
                    }
                }
            }   
        }

        /// <summary>
        /// Removes all Columns
        /// </summary>
        public void ClearColumns()
        {
            _columns = null;
            _columnIndexes.Clear();
        }


        /// <summary>
        /// Gets the Index of the Row with the specified Primary Key (null if not found)
        /// </summary>
        public int? GetRowIndex(TPrimaryKey primaryKeyValue)
        {
            if (primaryKeyValue != null)
            {
                if (_rowIndexes.TryGetValue(primaryKeyValue, out var index))
                {
                    return index;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the Primary Key of the Row at the specified Index (default if the Index is out of range)
        /// </summary>
        public TPrimaryKey GetRowPrimaryKey(int rowIndex)
        {
            if (rowIndex >= 0 && rowIndex < RowCount)
            {
                return (TPrimaryKey)_rows[rowIndex, 0];
            }

            return default;
        }

        /// <summary>
        /// Gets all of the Cell values for the Row with the specified Primary Key (null if not found)
        /// </summary>
        public object[] GetRowValues(TPrimaryKey primaryKeyValue)
        {
            if (primaryKeyValue != null)
            {
                if (_rowIndexes.TryGetValue(primaryKeyValue, out var rowIndex))
                {
                    var rowValues = new object[ColumnCount];
                    for (var i = 0; i < ColumnCount; i++)
                    {
                        rowValues[i] = _rows[rowIndex, i];
                    }

                    return rowValues;
                }
            }

            return null;
        }

        /// <summary>
        /// Sets the Cell value for the specified Primary Key and Column ID, adding a new Row if the Primary Key doesn't exist (ignored if the Column doesn't exist)
        /// </summary>
        public void AddRowValue(TPrimaryKey primaryKeyValue, string columnId, object cellValue)
        {
            var columnIndex = GetColumnIndex(columnId);
            if (columnIndex >= 0)
            {
                var rowIndex = GetRowIndex(primaryKeyValue) ?? -1;
                if (rowIndex >= 0)
                {
                    _rows[rowIndex, columnIndex] = cellValue;
                }
                else
                {
                    if (_rows != null)
                    {
                        var rowCount = _rows.Length / _columns.Length;
                        _rows = ResizeArray<object>(_rows, rowCount + 1, _columns.Length);
                    }
                    else
                    {
                        _rows = new object[1, _columns.Length];
                    }

                    rowIndex = _rows.Length - 1;
                    rowIndex = rowIndex / _columns.Length;

                    _rows[rowIndex, 0] = primaryKeyValue;
                    _rows[rowIndex, columnIndex] = cellValue;

                    _rowIndexes.Add(primaryKeyValue, rowIndex);
                }
            }
        }

        /// <summary>
        /// Removes all Rows
        /// </summary>
        public void ClearRows()
        {
            _rows = null;
            _rowIndexes.Clear();
        }


        /// <summary>
        /// Sorts the Rows by Primary Key (ascending)
        /// </summary>
        public void Sort()
        {
            if (_columns != null && _rows != null)
            {
                var rowCount = _rows.Length / _columns.Length;

                // Create Array of Primary Key Values
                var rowIndexes = new Dictionary<TPrimaryKey, int>(rowCount);
                var sortedKeys = new TPrimaryKey[rowCount];
                for (var i = 0; i < rowCount; i++)
                {
                    rowIndexes.Add((TPrimaryKey)_rows[i, 0], i);
                    sortedKeys[i] = (TPrimaryKey)_rows[i, 0];
                }

                // Sort Primary Keys
                Array.Sort(sortedKeys);

                var buffer = new object[_columns.Length];
                var existingRowIndex = 0;

                for (var i = 0; i < rowCount; i++)
                {
                    // Get Sorted Primary Key Value
                    var key = sortedKeys[i];

                    // Add Row[i] to buffer
                    existingRowIndex = rowIndexes[key];

                    // Get Original Primary Key Value
                    var originalKey = (TPrimaryKey)_rows[i, 0];

                    //existingRowIndex = GetRowIndex(x);
                    for (var j = 0; j < _columns.Length; j++)
                    {
                        buffer[j] = _rows[i, j];
                    }

                    // Swap row cells with one that was added to buffer
                    for (var j = 0; j < _columns.Length; j++)
                    {
                        _rows[i, j] = _rows[existingRowIndex, j];
                    }

                    // Update Existing Row from buffer
                    for (var j = 0; j < _columns.Length; j++)
                    {
                        _rows[existingRowIndex, j] = buffer[j];
                    }

                    // Update Row Index
                    rowIndexes[key] = i;
                    rowIndexes[originalKey] = existingRowIndex;
                }
            }
        }

        /// <summary>
        /// Fills empty (null) Cells with the most recent non-null value from a previous Row in the same Column
        /// </summary>
        public void Align()
        {
            if (_columns != null && _rows != null)
            {
                var rowCount = _rows.Length / _columns.Length;
                var buffer = new object[_columns.Length - 1];
                var bufferIndex = 0;

                for (var i = 0; i < rowCount; i++)
                {
                    bufferIndex = 0;
                    for (var j = 1; j < _columns.Length; j++)
                    {
                        var x = _rows[i, j];
                        if (x == null)
                        {
                            _rows[i, j] = buffer[bufferIndex];
                        }
                        else
                        {
                            buffer[bufferIndex] = _rows[i, j];
                        }

                        bufferIndex++;
                    }
                }
            }
        }


        private static T[,] ResizeArray<T>(T[,] original, int rows, int cols)
        {
            var newArray = new T[rows, cols];
            int minRows = Math.Min(rows, original.GetLength(0));
            int minCols = Math.Min(cols, original.GetLength(1));
            for (int i = 0; i < minRows; i++)
                for (int j = 0; j < minCols; j++)
                    newArray[i, j] = original[i, j];
            return newArray;
        }
    }
}
