// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http.Features;
using System.IO.Pipelines;

namespace TrakHound.Http
{
    /// <summary>
    /// Counts the Request Body bytes read (passes everything through to the original Body)
    /// </summary>
    internal sealed class CountingRequestStream : Stream
    {
        private readonly Stream _inner;
        private long _count;


        public long Count => Interlocked.Read(ref _count);


        public CountingRequestStream(Stream inner)
        {
            _inner = inner;
        }


        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Add(_inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Add(_inner.Read(buffer));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Add(await _inner.ReadAsync(buffer, offset, count, cancellationToken));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Add(await _inner.ReadAsync(buffer, cancellationToken));

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Add(int read)
        {
            if (read > 0) Interlocked.Add(ref _count, read);
            return read;
        }
    }

    /// <summary>
    /// Counts the Response Body bytes written through the Stream, the PipeWriter, or SendFile (passes everything through to the original Feature)
    /// </summary>
    internal sealed class CountingResponseBodyFeature : IHttpResponseBodyFeature
    {
        private readonly IHttpResponseBodyFeature _inner;
        private CountingResponseStream _stream;
        private CountingPipeWriter _writer;
        private long _count;


        public IHttpResponseBodyFeature Inner => _inner;

        public long Count => Interlocked.Read(ref _count);

        public Stream Stream => _stream ??= new CountingResponseStream(_inner.Stream, this);

        public PipeWriter Writer => _writer ??= new CountingPipeWriter(_inner.Writer, this);


        public CountingResponseBodyFeature(IHttpResponseBodyFeature inner)
        {
            _inner = inner;
        }


        internal void Add(long bytes)
        {
            if (bytes > 0) Interlocked.Add(ref _count, bytes);
        }

        public void DisableBuffering() => _inner.DisableBuffering();

        public Task StartAsync(CancellationToken cancellationToken = default) => _inner.StartAsync(cancellationToken);

        public Task CompleteAsync() => _inner.CompleteAsync();

        public async Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
        {
            await _inner.SendFileAsync(path, offset, count, cancellationToken);

            try
            {
                Add(count ?? Math.Max(0, new FileInfo(path).Length - offset));
            }
            catch { }
        }
    }

    internal sealed class CountingResponseStream : Stream
    {
        private readonly Stream _inner;
        private readonly CountingResponseBodyFeature _feature;


        public CountingResponseStream(Stream inner, CountingResponseBodyFeature feature)
        {
            _inner = inner;
            _feature = feature;
        }


        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            _feature.Add(count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _inner.Write(buffer);
            _feature.Add(buffer.Length);
        }

        public override void WriteByte(byte value)
        {
            _inner.WriteByte(value);
            _feature.Add(1);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _inner.WriteAsync(buffer, offset, count, cancellationToken);
            _feature.Add(count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(buffer, cancellationToken);
            _feature.Add(buffer.Length);
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    internal sealed class CountingPipeWriter : PipeWriter
    {
        private readonly PipeWriter _inner;
        private readonly CountingResponseBodyFeature _feature;


        public CountingPipeWriter(PipeWriter inner, CountingResponseBodyFeature feature)
        {
            _inner = inner;
            _feature = feature;
        }


        public override void Advance(int bytes)
        {
            _inner.Advance(bytes);
            _feature.Add(bytes);
        }

        public override Memory<byte> GetMemory(int sizeHint = 0) => _inner.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint = 0) => _inner.GetSpan(sizeHint);

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) => _inner.FlushAsync(cancellationToken);

        public override async ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
        {
            var result = await _inner.WriteAsync(source, cancellationToken);
            _feature.Add(source.Length);
            return result;
        }

        public override void CancelPendingFlush() => _inner.CancelPendingFlush();

        public override void Complete(Exception exception = null) => _inner.Complete(exception);

        public override ValueTask CompleteAsync(Exception exception = null) => _inner.CompleteAsync(exception);

        public override bool CanGetUnflushedBytes => _inner.CanGetUnflushedBytes;

        public override long UnflushedBytes => _inner.UnflushedBytes;
    }
}
