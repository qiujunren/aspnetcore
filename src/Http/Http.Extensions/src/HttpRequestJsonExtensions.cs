// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

#nullable enable

namespace Microsoft.AspNetCore.Http.Json
{
    public static class HttpRequestJsonExtensions
    {
        [return: MaybeNull]
        public static ValueTask<TValue> ReadFromJsonAsync<TValue>(
            this HttpRequest request,
            CancellationToken cancellationToken = default)
        {
            return request.ReadFromJsonAsync<TValue>(options: null, cancellationToken);
        }

        [return: MaybeNull]
        public static async ValueTask<TValue> ReadFromJsonAsync<TValue>(
            this HttpRequest request,
            JsonSerializerOptions? options,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!request.HasJsonContentType(out var charset))
            {
                throw CreateContentTypeError(request);
            }

            if (options == null)
            {
                options = ResolveSerializerOptions(request.HttpContext);
            }

            var encoding = GetEncodingFromCharset(charset);
            var (inputStream, usesTranscodingStream) = GetInputStream(request.HttpContext, encoding);

            try
            {
                return await JsonSerializer.DeserializeAsync<TValue>(inputStream, options, cancellationToken);
            }
            finally
            {
                if (usesTranscodingStream)
                {
                    await inputStream.DisposeAsync();
                }
            }
        }

        public static ValueTask<object?> ReadFromJsonAsync(
            this HttpRequest request,
            Type type,
            CancellationToken cancellationToken = default)
        {
            return request.ReadFromJsonAsync(type, options: null, cancellationToken);
        }

        public static async ValueTask<object?> ReadFromJsonAsync(
            this HttpRequest request,
            Type type,
            JsonSerializerOptions? options,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (!request.HasJsonContentType(out var charset))
            {
                throw CreateContentTypeError(request);
            }

            if (options == null)
            {
                options = ResolveSerializerOptions(request.HttpContext);
            }

            var encoding = GetEncodingFromCharset(charset);
            var (inputStream, usesTranscodingStream) = GetInputStream(request.HttpContext, encoding);

            try
            {
                return await JsonSerializer.DeserializeAsync(inputStream, type, options, cancellationToken);
            }
            finally
            {
                if (usesTranscodingStream)
                {
                    await inputStream.DisposeAsync();
                }
            }
        }

        private static JsonSerializerOptions ResolveSerializerOptions(HttpContext httpContext)
        {
            // Attempt to resolve options from DI then fallback to default options
            return httpContext.RequestServices?.GetService<IOptions<JsonOptions>>()?.Value?.SerializerOptions ?? JsonOptions.DefaultSerializerOptions;
        }

        private static InvalidOperationException CreateContentTypeError(HttpRequest request)
        {
            return new InvalidOperationException($"Unable to read the request as JSON because the request content type '{request.ContentType}' is not a known JSON content type.");
        }

        private static (Stream inputStream, bool usesTranscodingStream) GetInputStream(HttpContext httpContext, Encoding? encoding)
        {
            if (encoding == null || encoding.CodePage == Encoding.UTF8.CodePage)
            {
                return (httpContext.Request.Body, false);
            }

            var inputStream = Encoding.CreateTranscodingStream(httpContext.Request.Body, encoding, Encoding.UTF8, leaveOpen: true);
            return (inputStream, true);
        }

        private static Encoding? GetEncodingFromCharset(StringSegment charset)
        {
            if (charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
            {
                // This is an optimization for utf-8 that prevents the Substring caused by
                // charset.Value
                return Encoding.UTF8;
            }

            try
            {
                // charset.Value might be an invalid encoding name as in charset=invalid.
                return charset.HasValue ? Encoding.GetEncoding(charset.Value) : null;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unable to resolve charset '{charset}' to a known encoding.", ex);
            }
        }
    }
}