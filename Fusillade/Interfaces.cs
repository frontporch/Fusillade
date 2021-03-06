﻿using System;
using System.Net.Http;
using Punchclock;
using Splat;
using System.Threading;
using System.Threading.Tasks;

namespace Fusillade
{
    /// <summary>
    /// This enumeration defines the default base priorities associated with the
    /// different NetCache instances
    /// </summary>
    public enum Priority {
        Speculative = 10,
        UserInitiated = 100,
        Background = 20,
        Explicit = 0,
    }

    /// <summary>
    /// Limiting HTTP schedulers only allow a certain number of bytes to be
    /// read before cancelling all future requests. This is designed for
    /// reading data that may or may not be used by the user later, in order
    /// to improve response times should the user later request the data.
    /// </summary>
    public abstract class LimitingHttpMessageHandler : DelegatingHandler
    {
        public LimitingHttpMessageHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }
        public LimitingHttpMessageHandler() : base() { }

        /// <summary>
        /// Resets the total limit of bytes to read. This is usually called
        /// when the app resumes from suspend, to indicate that we should
        /// fetch another set of data.
        /// </summary>
        /// <param name="maxBytesToRead"></param>
        public abstract void ResetLimit(long? maxBytesToRead = null);
    }

    /// <summary>
    /// This Interface is a simple cache for HTTP requests - it is intentionally
    /// *not* designed to conform to HTTP caching rules since you most likely want
    /// to override those rules in a client app anyways.
    /// </summary>
    public interface IRequestCache
    {
        /// <summary>
        /// Implement this method by saving the Body of the response. The 
        /// response is already downloaded as a ByteArrayContent so you don't
        /// have to worry about consuming the stream.
        /// </summary>
        /// <param name="request">The originating request.</param>
        /// <param name="response">The response whose body you should save.</param>
        /// <param name="key">A unique key used to identify the request details.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Completion.</returns>
        Task Save(HttpRequestMessage request, HttpResponseMessage response, string key, CancellationToken ct);

        /// <summary>
        /// Implement this by loading the Body of the given request / key.
        /// </summary>
        /// <param name="request">The originating request.</param>
        /// <param name="key">A unique key used to identify the request details, 
        /// that was given in Save().</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The Body of the given request, or null if the search 
        /// completed successfully but the response was not found.</returns>
        Task<byte[]> Fetch(HttpRequestMessage request, string key, CancellationToken ct);
    }

    public static class NetCache
    {
        static NetCache()
        {
            var innerHandler = Locator.Current.GetService<HttpMessageHandler>() ?? new HttpClientHandler();

            // NB: In vNext this value will be adjusted based on the user's
            // network connection, but that requires us to go fully platformy
            // like Splat.
            speculative = new RateLimitedHttpMessageHandler(innerHandler, Priority.Speculative, 0, 1048576 * 5);
            userInitiated = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, 0);
            background = new RateLimitedHttpMessageHandler(innerHandler, Priority.Background, 0);
            offline = new OfflineHttpMessageHandler(null);
        }

        static LimitingHttpMessageHandler speculative;
        [ThreadStatic] static LimitingHttpMessageHandler unitTestSpeculative;

        /// <summary>
        /// Speculative HTTP schedulers only allow a certain number of bytes to be
        /// read before cancelling all future requests. This is designed for
        /// reading data that may or may not be used by the user later, in order
        /// to improve response times should the user later request the data.
        /// </summary>
        public static LimitingHttpMessageHandler Speculative
        {
            get { return unitTestSpeculative ?? speculative ?? Locator.Current.GetService<LimitingHttpMessageHandler>("Speculative"); }
            set {
                if (ModeDetector.InUnitTestRunner()) {
                    unitTestSpeculative = value;
                    speculative = speculative ?? value;
                } else {
                    speculative = value;
                }
            }
        }
                
        static HttpMessageHandler userInitiated;
        [ThreadStatic] static HttpMessageHandler unitTestUserInitiated;

        /// <summary>
        /// This scheduler should be used for requests initiated by a user
        /// action such as clicking an item, they have the highest priority.
        /// </summary>
        public static HttpMessageHandler UserInitiated
        {
            get { return unitTestUserInitiated ?? userInitiated ?? Locator.Current.GetService<HttpMessageHandler>("UserInitiated"); }
            set {
                if (ModeDetector.InUnitTestRunner()) {
                    unitTestUserInitiated = value;
                    userInitiated = userInitiated ?? value;
                } else {
                    userInitiated = value;
                }
            }
        }

        static HttpMessageHandler background;
        [ThreadStatic] static HttpMessageHandler unitTestBackground;

        /// <summary>
        /// This scheduler should be used for requests initiated in the
        /// background, and are scheduled at a lower priority.
        /// </summary>
        public static HttpMessageHandler Background
        {
            get { return unitTestBackground ?? background ?? Locator.Current.GetService<HttpMessageHandler>("Background"); }
            set {
                if (ModeDetector.InUnitTestRunner()) {
                    unitTestBackground = value;
                    background = background ?? value;
                } else {
                    background = value;
                }
            }
        }

        static HttpMessageHandler offline;
        [ThreadStatic] static HttpMessageHandler unitTestOffline;

        /// <summary>
        /// This scheduler fetches results solely from the cache specified in
        /// RequestCache.
        /// </summary>
        public static HttpMessageHandler Offline {
            get { return unitTestOffline ?? offline ?? Locator.Current.GetService<HttpMessageHandler>("Offline"); }
            set {
                if (ModeDetector.InUnitTestRunner()) {
                    unitTestOffline = value;
                    offline = offline ?? value;
                } else {
                    offline = value;
                }
            }
        }

        static OperationQueue operationQueue = new OperationQueue(4);
        [ThreadStatic] static OperationQueue unitTestOperationQueue;

        /// <summary>
        /// This scheduler should be used for requests initiated in the
        /// operationQueue, and are scheduled at a lower priority. You don't
        /// need to mess with this.
        /// </summary>
        public static OperationQueue OperationQueue
        {
            get { return unitTestOperationQueue ?? operationQueue ?? Locator.Current.GetService<OperationQueue>("OperationQueue"); }
            set {
                if (ModeDetector.InUnitTestRunner()) {
                    unitTestOperationQueue = value;
                    operationQueue = operationQueue ?? value;
                } else {
                    operationQueue = value;
                }
            }
        }

        static IRequestCache requestCache;
        [ThreadStatic] static IRequestCache unitTestRequestCache;

        /// <summary>
        /// If set, this indicates that HTTP handlers should save and load 
        /// requests from a cached source.
        /// </summary>
        public static IRequestCache RequestCache
        {
            get { return unitTestRequestCache ?? requestCache; } 
            set {
                if (ModeDetector.InUnitTestRunner()) {
                    unitTestRequestCache = value;
                    requestCache = requestCache ?? value;
                } else {
                    requestCache = value;
                }
            }
        }
    }
}