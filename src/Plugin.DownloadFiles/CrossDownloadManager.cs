using Plugin.DownloadFiles.Abstractions;
using System;

namespace Plugin.DownloadFiles
{
    /// <summary>
    /// Cross platform DownloadManager implemenations
    /// </summary>
    public class CrossDownloadManager
    {
        private static Lazy<IDownloadManager> Implementation = new Lazy<IDownloadManager>(() => CreateDownloadManager(), System.Threading.LazyThreadSafetyMode.PublicationOnly);

#if __IOS__
        /// <summary>
        /// Set the background session completion handler.
        /// @see: https://developer.xamarin.com/guides/ios/application_fundamentals/backgrounding/part_4_ios_backgrounding_walkthroughs/background_transfer_walkthrough/#Handling_Transfer_Completion
        /// </summary>
        public static Action BackgroundSessionCompletionHandler;

        /// <summary>
        /// The URL session download delegate.
        /// @see https://developer.apple.com/library/ios/documentation/Foundation/Reference/NSURLSessionDownloadDelegate_protocol/#//apple_ref/occ/intfm/NSURLSessionDownloadDelegate/URLSession:downloadTask:didResumeAtOffset:expectedTotalBytes:
        /// </summary>
        public static UrlSessionDownloadDelegate UrlSessionDownloadDelegate;

        /// <summary>
        /// Wether you should use a normal download session configuration instead of as background download session configuration when the app is in the background to avoid the discretionary.
        /// This makes the app download in the same process to be able to download immediately instead of waiting for the systems scheduling algorithm.
        /// The download will however not continue if the app is suspended while downloading.
        /// @see https://developer.apple.com/documentation/foundation/nsurlsessionconfiguration/1411552-discretionary?language=objc
        /// </summary>
        public static bool AvoidDiscretionaryDownloadInBackground;
        
        /// <summary>
        /// Set the HttpMaximumConnectionsPerHost for the NSUrlSessionConfiguration
        /// It is recommended to leave this setting on it's default 1, as higher values might cause higher memory usage. However there are situations
        /// where a higher value could make sense.
        /// </summary>
#endif

        /// <summary>
        /// The platform-implementation
        /// </summary>
        public static IDownloadManager Current
        {
            get
            {
                var ret = Implementation.Value;
                if (ret == null)
                {
                    throw NotImplementedInReferenceAssembly();
                }
                return ret;
            }
        }

        private static IDownloadManager CreateDownloadManager()
        {
#if __IOS__
            return new DownloadManagerImplementation (UrlSessionDownloadDelegate ?? new UrlSessionDownloadDelegate(), AvoidDiscretionaryDownloadInBackground);
#elif __ANDROID__ || __UNIFIED__ || WINDOWS_UWP
            return new DownloadManagerImplementation();
#else
            return null;
#endif
        }

        internal static Exception NotImplementedInReferenceAssembly()
        {
            return new NotImplementedException("This functionality is not implemented in the portable version of this assembly.  You should reference the NuGet package from your main application project in order to reference the platform-specific implementation.");
        }
    }
}
