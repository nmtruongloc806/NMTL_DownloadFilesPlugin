using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Foundation;
using Plugin.DownloadFiles.Abstractions;
using UIKit;

namespace Plugin.DownloadFiles
{
    public class DownloadManagerImplementation : IDownloadManager
    {
        public const string DownloadFile_NotifyKey = "DownloadFile_Notify";
        private string _identifier => NSBundle.MainBundle.BundleIdentifier + ".BackgroundTransferSession";

        private readonly bool _avoidDiscretionaryDownloadInBackground;

        private readonly NSUrlSession _backgroundSession;

        private readonly NSUrlSession _session;

        private readonly IList<IDownloadFile> _queue;
        public IEnumerable<IDownloadFile> Queue
        {
            get
            {
                lock (_queue)
                {
                    return _queue.ToList();
                }
            }
        }
        public Func<IDownloadFile, string> PathNameForDownloadedFile { get; set; }
        public DownloadManagerImplementation(UrlSessionDownloadDelegate sessionDownloadDelegate,
            bool avoidDiscretionaryDownloadInBackground)
        {
            _avoidDiscretionaryDownloadInBackground = avoidDiscretionaryDownloadInBackground;
            _queue = new List<IDownloadFile>();

            if (avoidDiscretionaryDownloadInBackground)
            {
                _session = InitDefaultSession(sessionDownloadDelegate);
            }

            _backgroundSession = InitBackgroundSession(sessionDownloadDelegate);

            // Reinitialize tasks that were started before the app was terminated or suspended
            _backgroundSession.GetTasks2((dataTasks, uploadTasks, downloadTasks) => {
                foreach (var task in downloadTasks)
                {
                    AddFile(new DownloadFileImplementation(task));
                }
            });
        }
        public IDownloadFile CreateDownloadFile(string url)
        {
            return CreateDownloadFile(url, new Dictionary<string, string>());
        }
        public IDownloadFile CreateDownloadFile(string url, IDictionary<string, string> headers)
        {
            return new DownloadFileImplementation(url, headers);
        }

        public void StartDownloadManager()
        {
            var downloads = Queue.Cast<DownloadFileImplementation>().ToList();
            var file = downloads.FirstOrDefault();
            if (file != null)
            {
                NSOperationQueue.MainQueue.BeginInvokeOnMainThread(() =>
                {
                    NSUrlSession session;

                    var inBackground = UIApplication.SharedApplication.ApplicationState == UIApplicationState.Background;

                    if (_avoidDiscretionaryDownloadInBackground && inBackground)
                    {
                        session = _session;
                    }
                    else
                    {
                        session = _backgroundSession;
                    }

                    file.StartDownload(session, true);
                });
            }
        }

        public void Start(IDownloadFile i, bool mobileNetworkAllowed = true)
        {
            var downloadFile = (DownloadFileImplementation)i;
            var downloads = Queue.Cast<DownloadFileImplementation>().ToList();
            var item = downloads.Where(x => x.Url == downloadFile.Url).FirstOrDefault();
            if (item != null)
            {
                Abort(item);
            }
            else
            {
                if (FileExistenceCheck(i))
                    return;

                AddFile(downloadFile);

                downloadFile.Status = DownloadFileStatus.INITIALIZED;

                if (downloads.Count == 0)
                {
                    StartDownloadManager();
                }
            }
        }

        private bool FileExistenceCheck(IDownloadFile i)
        {
            var downloadFile = (DownloadFileImplementation)i;
            var fileManager = NSFileManager.DefaultManager;
            var destinationPathName = downloadFile.DestinationPathName;

            string nameFile = destinationPathName.Split('/').Last();
            var URLs = fileManager.GetUrls(NSSearchPathDirectory.DocumentDirectory, NSSearchPathDomain.User);
            NSUrl documentsDictionry = URLs.First();
            NSUrl destinationURL = documentsDictionry.Append(nameFile, false);

            if (fileManager.FileExists(destinationURL.Path))
            {
                downloadFile.DestinationPathName = destinationURL.Path;
                downloadFile.StatusDetails = default(string);
                downloadFile.Status = DownloadFileStatus.COMPLETED;
                return true;
            }
            return false;
        }

        public void Abort(DownloadFileImplementation file)
        {
            file.Status = DownloadFileStatus.CANCELED;
            file.Task?.Cancel();

            RemoveFile(file);
        }
        /**
         * We initialize the background session with the following options
         * - nil as queue: The method, called on events could end up on any thread
         * - Only one connection per host
         */
        NSUrlSession InitBackgroundSession(UrlSessionDownloadDelegate sessionDownloadDelegate)
        {
            sessionDownloadDelegate.Controller = this;

            NSUrlSessionConfiguration configuration;

            if (UIDevice.CurrentDevice.CheckSystemVersion(8, 0))
            {
                configuration = NSUrlSessionConfiguration.CreateBackgroundSessionConfiguration(_identifier);
            }
            else
            {
                configuration = NSUrlSessionConfiguration.BackgroundSessionConfiguration(_identifier);
            }
            return InitSession(sessionDownloadDelegate, configuration);
        }
        NSUrlSession InitDefaultSession(UrlSessionDownloadDelegate sessionDownloadDelegate)
        {
            return InitSession(sessionDownloadDelegate, NSUrlSessionConfiguration.DefaultSessionConfiguration);
        }
        NSUrlSession InitSession(UrlSessionDownloadDelegate sessionDownloadDelegate,
            NSUrlSessionConfiguration configuration)
        {
            sessionDownloadDelegate.Controller = this;

            using (configuration)
            {
                return createSession(configuration, sessionDownloadDelegate);
            }
        }
        private NSUrlSession createSession(NSUrlSessionConfiguration configuration, UrlSessionDownloadDelegate sessionDownloadDelegate)
        {
            configuration.HttpMaximumConnectionsPerHost = 1;
            configuration.AllowsCellularAccess = true;
            return NSUrlSession.FromConfiguration(configuration, sessionDownloadDelegate, null);
        }
        protected internal void AddFile(IDownloadFile file)
        {
            lock (_queue)
            {
                _queue.Add(file);
            }
        }
        protected internal void RemoveFile(IDownloadFile file)
        {
            lock (_queue)
            {
                _queue.Remove(file);
            }
        }
    }
}