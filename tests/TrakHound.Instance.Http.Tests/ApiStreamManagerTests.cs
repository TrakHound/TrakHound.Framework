// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Instance.Api;

namespace TrakHound.Instance.Http.Tests
{
    public class ApiStreamManagerTests
    {
        private static TrakHoundStream<TrakHoundApiResponse> Open(TrakHoundApiStreamManager manager, string path)
        {
            var stream = new TrakHoundStream<TrakHoundApiResponse>(Guid.NewGuid().ToString());
            manager.Add(stream, new TrakHoundApiStreamInformation { ApiId = "api-1", Path = path, Opened = DateTime.UtcNow });
            return stream;
        }

        [Fact]
        public async Task Counts_Delivered_Messages()
        {
            var manager = new TrakHoundApiStreamManager();
            var stream = Open(manager, "/runs/1/status");

            await stream.Push(TrakHoundApiResponse.Ok("abc"));
            await stream.Push(TrakHoundApiResponse.Ok("de"));

            var open = Assert.Single(manager.Get().Open);
            Assert.Equal(stream.Id, open.StreamId);
            Assert.Equal(2, open.MessageCount);
            Assert.True(open.ByteCount > 0);
            Assert.NotNull(open.LastMessage);
        }

        [Fact]
        public void Disposed_Streams_Are_Closed_By_Source()
        {
            var manager = new TrakHoundApiStreamManager();
            var stream = Open(manager, "/runs/1/status");

            stream.Dispose();

            var streams = manager.Get();
            Assert.Empty(streams.Open);
            var closed = Assert.Single(streams.Closed);
            Assert.Equal(TrakHoundApiStreamCloseReasons.ClosedBySource, closed.CloseReason);
            Assert.NotNull(closed.Closed);
        }

        [Fact]
        public void Close_Disposes_The_Stream_With_The_Admin_Reason()
        {
            var manager = new TrakHoundApiStreamManager();
            var stream = Open(manager, "/runs/1/status");

            Assert.True(manager.Close(stream.Id));
            Assert.True(stream.CancellationToken.IsCancellationRequested);

            var closed = Assert.Single(manager.Get().Closed);
            Assert.Equal(TrakHoundApiStreamCloseReasons.ClosedByAdmin, closed.CloseReason);

            // Already closed
            Assert.False(manager.Close(stream.Id));
        }

        [Fact]
        public void Close_Reason_Set_Before_Closing_Is_Kept()
        {
            var manager = new TrakHoundApiStreamManager();
            var stream = Open(manager, "/runs/1/status");

            manager.SetCloseReason(stream.Id, TrakHoundApiStreamCloseReasons.Stopped);
            stream.Dispose();

            Assert.Equal(TrakHoundApiStreamCloseReasons.Stopped, Assert.Single(manager.Get().Closed).CloseReason);
        }

        [Fact]
        public void Closed_History_Zero_Keeps_No_Closed_Streams()
        {
            var manager = new TrakHoundApiStreamManager { ClosedHistory = 0 };
            Open(manager, "/runs/1/status").Dispose();

            var streams = manager.Get();
            Assert.Equal(0, streams.ClosedCapacity);
            Assert.Empty(streams.Closed);
            Assert.Empty(streams.Open);
        }
    }
}
