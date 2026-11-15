// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TrakHound
{
    /// <summary>
    /// A block of time (Start / End) during which a Key (ex. a state value) was active, used to build timelines
    /// </summary>
    public class TrakHoundBlock
    {
        /// <summary>
        /// An optional Identifier of the item the Block was created from
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The Key (ex. a state value) that was active during the Block
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// An optional display Label for the Block
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// The start of the Block
        /// </summary>
        public DateTime Start { get; set; }

        /// <summary>
        /// The end of the Block
        /// </summary>
        public DateTime End { get; set; }

        /// <summary>
        /// The length of the Block (End - Start)
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                return End - Start;
            }
        }


        /// <summary>
        /// Returns true if the Block overlaps the specified range
        /// </summary>
        public bool IsBetween(DateTime from, DateTime to)
        {
            if (to > from && End > Start)
            {
                if (Start >= from && Start < to) return true;
                if (End >= from && End < to) return true;
                if (Start <= from && End >= to) return true;
            }

            return false;
        }


        /// <summary>
        /// Generates Blocks from point-in-time items, where each item is active until the next item's timestamp. Blocks are clipped to the from/to range and to now, Blocks shorter than minimumDuration are removed, and adjacent Blocks with the same Key are joined (null if there are no items)
        /// </summary>
        public static IEnumerable<TrakHoundBlock> Generate<T>(
            IEnumerable<T> items,
            Func<T, string> keySelector,
            Func<T, DateTime> timestampSelector,
            DateTime from,
            DateTime to,
            DateTime now,
            TimeSpan? minimumDuration = null,
            Func<T, string> idSelector = null,
            Func<T, string> labelSelector = null
            )
        {
            if (!items.IsNullOrEmpty() && now > from && from < to)
            {
                var blocks = new List<TrakHoundBlock>();

                var filtered = new List<T>();
                var fItems = items.Where(o => timestampSelector(o) >= from && timestampSelector(o) <= to);
                if (!fItems.IsNullOrEmpty()) filtered.AddRange(fItems);
                if (!filtered.Any(o => timestampSelector(o) == from))
                {
                    var lastFound = items.Where(o => timestampSelector(o) < from);
                    if (!lastFound.IsNullOrEmpty()) filtered.Add(lastFound.OrderByDescending(o => timestampSelector(o)).FirstOrDefault());
                }

                if (!filtered.IsNullOrEmpty())
                {
                    var ordered = filtered.OrderBy(o => timestampSelector(o)).ToList();
                    var prev = ordered[0];
                    var max = ordered.Count - 1;

                    if (ordered.Count > 1) // Multiple Item Found
                    {
                        for (var i = 1; i <= max; i++)
                        {
                            var current = ordered[i];
                            var start = timestampSelector(prev) > from ? timestampSelector(prev) : from;
                            var stop = timestampSelector(current) < to ? timestampSelector(current) : to;
                            if (stop > now) stop = now;


                            var block = new TrakHoundBlock();
                            block.Key = keySelector(prev);
                            if (idSelector != null) block.Id = idSelector(prev);
                            if (labelSelector != null) block.Label = labelSelector(prev);
                            block.Start = start;
                            block.End = stop;
                            blocks.Add(block);

                            prev = current;
                        }

                        var lastStart = timestampSelector(prev) > from ? timestampSelector(prev) : from;
                        var lastStop = now < to ? now : to;

                        var lastBlock = new TrakHoundBlock();
                        lastBlock.Key = keySelector(prev);
                        if (idSelector != null) lastBlock.Id = idSelector(prev);
                        if (labelSelector != null) lastBlock.Label = labelSelector(prev);
                        lastBlock.Start = lastStart;
                        lastBlock.End = lastStop;
                        blocks.Add(lastBlock);
                    }
                    else // Only one Item Found
                    {
                        var onlyStart = timestampSelector(prev) > from ? timestampSelector(prev) : from;
                        var onlyStop = now < to ? now : to;

                        var onlyObj = new TrakHoundBlock();
                        onlyObj.Key = keySelector(prev);
                        if (idSelector != null) onlyObj.Id = idSelector(prev);
                        if (labelSelector != null) onlyObj.Label = labelSelector(prev);
                        onlyObj.Start = onlyStart;
                        onlyObj.End = onlyStop;
                        blocks.Add(onlyObj);
                    }
                }

                // Filter out blocks that don't meet the Minimum Duration
                var filteredBlocks = new List<TrakHoundBlock>();
                if (minimumDuration != null)
                {
                    foreach (var block in blocks)
                    {
                        if (block.Duration >= minimumDuration)
                        {
                            filteredBlocks.Add(block);
                        }
                    }
                }
                else
                {
                    filteredBlocks = blocks;
                }

                // Process Blocks to join empty spaces
                var processedBlocks = new List<TrakHoundBlock>();
                TrakHoundBlock prevBlock = null;
                TrakHoundBlock currentBlock = null;
                for (var i = 0; i < filteredBlocks.Count; i++)
                {
                    currentBlock = filteredBlocks[i];
                    if (prevBlock != null)
                    {
                        if (currentBlock.Key == prevBlock.Key)
                        {
                            // This "removes" adjacent blocks with the same key
                            prevBlock.End = currentBlock.End;
                        }
                        else
                        {
                            // Fill any gaps left from filtering
                            prevBlock.End = currentBlock.Start;
                            processedBlocks.Add(prevBlock);
                            prevBlock = currentBlock;
                        }
                    }
                    else
                    {
                        prevBlock = currentBlock;
                    }
                }

                // Add Last Block
                if (prevBlock != null) processedBlocks.Add(prevBlock);

                return processedBlocks;
            }

            return null;
        }

        /// <summary>
        /// Generates Blocks from items that have their own start and end timestamps, clipped to the from/to range and to now (null if there are no items)
        /// </summary>
        public static IEnumerable<TrakHoundBlock> Generate<T>(
            IEnumerable<T> items,
            Func<T, string> keySelector,
            Func<T, DateTime> startTimestampSelector,
            Func<T, DateTime> endTimestampSelector,
            DateTime from,
            DateTime to,
            DateTime now,
            Func<T, string> idSelector = null,
            Func<T, string> labelSelector = null
            )
        {
            if (!items.IsNullOrEmpty() && from < to)
            {
                var blocks = new List<TrakHoundBlock>();

                var filtered = new List<T>();
                var fItems = items.Where(o => startTimestampSelector(o) >= from && endTimestampSelector(o) <= DateTime.MinValue || startTimestampSelector(o) >= from && startTimestampSelector(o) < to || startTimestampSelector(o) <= from && endTimestampSelector(o) >= to || startTimestampSelector(o) >= from && endTimestampSelector(o) <= to || startTimestampSelector(o) <= from && endTimestampSelector(o) > from && endTimestampSelector(o) < to);
                if (!fItems.IsNullOrEmpty()) filtered.AddRange(fItems);

                if (!filtered.IsNullOrEmpty())
                {
                    foreach (var item in filtered)
                    {
                        var start = startTimestampSelector(item) > from ? startTimestampSelector(item) : from;
                        var stop = endTimestampSelector(item) < to && endTimestampSelector(item) > DateTime.MinValue ? endTimestampSelector(item) : to;

                        var block = new TrakHoundBlock();
                        block.Key = keySelector(item);
                        if (idSelector != null) block.Id = idSelector(item);
                        if (labelSelector != null) block.Label = labelSelector(item);
                        block.Start = start;
                        block.End = stop > now ? now : stop;
                        blocks.Add(block);
                    }
                }

                return blocks;
            }

            return null;
        }
    }
}
