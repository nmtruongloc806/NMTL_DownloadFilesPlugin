using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Plugin.DownloadFiles.Abstractions;

namespace Plugin.DownloadFiles
{
    public class DownloadManagerImplementation : IDownloadManager
    {
        private Android.OS.Handler _downloadWatcherHandler;
        private Java.Lang.Runnable _downloadWatcherHandlerRunnable;
        private readonly Android.App.DownloadManager _downloadManager;
        public const string DownloadFile_NotifyKey = "DownloadFile_Notify";

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

        public DownloadVisibility NotificationVisibility;
        public bool IsVisibleInDownloadsUi { get; set; } = true; // true is the default behavior from Android DownloadManagerApi

        public DownloadManagerImplementation()
        {
            _queue = new List<IDownloadFile>();
            _downloadManager = (Android.App.DownloadManager)Android.App.Application.Context.GetSystemService(Context.DownloadService);
            //StartDownloadWatcher();
        }

        private void StartDownloadWatcher()
        {
            if (_downloadWatcherHandler != null)
                return;

            // Create an instance for a runnable-handler
            _downloadWatcherHandler = new Android.OS.Handler();

            // Create a runnable, restarting itself to update every file in the queue
            _downloadWatcherHandlerRunnable = new Java.Lang.Runnable(() => {
                var queueDownload = Queue.Cast<DownloadFileImplementation>();
                var file = queueDownload.FirstOrDefault();
                if (file != null)
                {
                    if (file.Status == DownloadFileStatus.PAUSED)
                    {
                        var fileTemp = queueDownload.FirstOrDefault(x => x.Status != DownloadFileStatus.PAUSED);
                        if (fileTemp != null)
                            file = fileTemp;
                    }

                    if (file.Status == DownloadFileStatus.INITIALIZED)
                    {
                        string destinationPathName = null;
                        if (PathNameForDownloadedFile != null)
                        {
                            destinationPathName = PathNameForDownloadedFile(file);
                        }
                        file.StartDownload(_downloadManager, destinationPathName, true, NotificationVisibility, IsVisibleInDownloadsUi);
                    }

                    var query = new Android.App.DownloadManager.Query();
                    query.SetFilterById(file.Id);
                    try
                    {
                        using (var cursor = _downloadManager.InvokeQuery(query))
                        {
                            if (cursor != null && cursor.MoveToNext())
                            {
                                UpdateFileProperties(cursor, file);
                            }
                            else
                            {
                                // This file is not listed in the native download manager anymore. Let's mark it as canceled.
                                Abort(file);
                            }
                            cursor?.Close();
                        }
                    }
                    catch (Android.Database.Sqlite.SQLiteException)
                    {
                        // I lately got an exception that the database was unaccessible ...
                    }
                }
                _downloadWatcherHandler?.PostDelayed(_downloadWatcherHandlerRunnable, 1000);
            });
            // Start this playing handler immediately
            _downloadWatcherHandler.Post(_downloadWatcherHandlerRunnable);
        }

        public void StopDownloadWatcher()
        {
            var downloads = Queue.Cast<DownloadFileImplementation>().ToList();
            if (downloads == null || downloads.Count == 0)
            {
                _downloadWatcherHandler.RemoveCallbacks(_downloadWatcherHandlerRunnable);
                _downloadWatcherHandler = null;
            }
        }

        public IDownloadFile CreateDownloadFile(string url)
        {
            return CreateDownloadFile(url, new Dictionary<string, string>());
        }

        public IDownloadFile CreateDownloadFile(string url, IDictionary<string, string> headers)
        {
            return new DownloadFileImplementation(url, headers);
        }

        public void Start(IDownloadFile i, bool mobileNetworkAllowed = true)
        {
            var downloadFile = (DownloadFileImplementation)i;

            //if(FileExistenceCheck(i))
            //    return;

            var downloads = Queue.Cast<DownloadFileImplementation>();
            var item = downloads.Where(x => x.Url == downloadFile.Url).FirstOrDefault();
            if (item != null)
            {
                Abort(item);
            }
            else
            {
                downloadFile.Status = DownloadFileStatus.INITIALIZED;

                AddFile(downloadFile);

                StartDownloadWatcher();
            }
        }

        private bool IsDownloading(DownloadFileImplementation downloadFile)
        {
            if (downloadFile == null) return false;

            switch (downloadFile.Status)
            {
                case DownloadFileStatus.INITIALIZED:
                case DownloadFileStatus.PAUSED:
                case DownloadFileStatus.PENDING:
                case DownloadFileStatus.RUNNING:
                    return true;

                case DownloadFileStatus.COMPLETED:
                case DownloadFileStatus.CANCELED:
                case DownloadFileStatus.FAILED:
                case DownloadFileStatus.NONE:
                    return false;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void Abort(DownloadFileImplementation file)
        {
            file.Status = DownloadFileStatus.CANCELED;
            _downloadManager.Remove(file.Id);
            RemoveFile(file);

        }
        private bool FileExistenceCheck(IDownloadFile i)
        {
            var downloadFile = (DownloadFileImplementation)i;

            var destinationPathName = PathNameForDownloadedFile(downloadFile);
            var file = new Java.IO.File(destinationPathName);
            if (file.Exists())
            {
                string mimeType = Android.Webkit.MimeTypeMap.Singleton.GetMimeTypeFromExtension(Android.Webkit.MimeTypeMap.GetFileExtensionFromUrl(destinationPathName.ToLower()));
                if (mimeType == null)
                    mimeType = "*/*";

                downloadFile.DestinationPathName = destinationPathName;
                downloadFile.MimeType = mimeType;
                downloadFile.StatusDetails = default(string);
                downloadFile.Status = DownloadFileStatus.COMPLETED;
                return true;
            }
            return false;
        }

        /**
         * Update the properties for a file by it's cursor.
         * This method should be called in an interval and on reinitialization.
         */
        public void UpdateFileProperties(ICursor cursor, DownloadFileImplementation downloadFile)
        {
            downloadFile.TotalBytesWritten = cursor.GetFloat(cursor.GetColumnIndex(Android.App.DownloadManager.ColumnBytesDownloadedSoFar));
            downloadFile.TotalBytesExpected = cursor.GetFloat(cursor.GetColumnIndex(Android.App.DownloadManager.ColumnTotalSizeBytes));
            string reason = cursor.GetString(cursor.GetColumnIndex("reason"));
            switch ((DownloadStatus)cursor.GetInt(cursor.GetColumnIndex(Android.App.DownloadManager.ColumnStatus)))
            {
                case DownloadStatus.Successful:
                    string title = cursor.GetString(cursor.GetColumnIndex("title"));
                    string pathFile = cursor.GetString(cursor.GetColumnIndex("local_uri"));
                    string type = cursor.GetString(cursor.GetColumnIndex("media_type"));
                    string path = pathFile.Replace("file://", "");
                    downloadFile.DestinationPathName = path;
                    downloadFile.MimeType = type;
                    downloadFile.StatusDetails = default(string);
                    downloadFile.Status = DownloadFileStatus.COMPLETED;
                    IDictionary<string, string> dict = new Dictionary<string, string>();
                    dict.Add(DownloadFile_NotifyKey, path);
                    dict.Add("type", type);
                    RemoveFile(downloadFile);
                    //CrossLocalNotifications_Droid.Current.PushNotify(
                    //    downloadFile.NameFile,
                    //     "Tải tập tin thành công",
                    //    new Action<NotificationResult>(result => {
                    //        if (result != null && result.Action == NotificationAction.Clicked)
                    //        {
                    //            IDocumentViewer documentViewer = DependencyService.Get<IDocumentViewer>();
                    //            documentViewer.OpenFileDownloaded(path, type);
                    //        }
                    //    }),
                    //    dict);
                    break;

                case DownloadStatus.Failed:
                    var reasonFailed = cursor.GetInt(cursor.GetColumnIndex(Android.App.DownloadManager.ColumnReason));
                    if (reasonFailed < 600)
                    {
                        downloadFile.StatusDetails = "Error.HttpCode: " + reasonFailed;
                    }
                    else
                    {
                        switch ((DownloadError)reasonFailed)
                        {
                            case DownloadError.CannotResume:
                                downloadFile.StatusDetails = "Error.CannotResume";
                                break;
                            case DownloadError.DeviceNotFound:
                                downloadFile.StatusDetails = "Error.DeviceNotFound";
                                break;
                            case DownloadError.FileAlreadyExists:
                                downloadFile.StatusDetails = "Error.FileAlreadyExists";
                                break;
                            case DownloadError.FileError:
                                downloadFile.StatusDetails = "Error.FileError";
                                break;
                            case DownloadError.HttpDataError:
                                downloadFile.StatusDetails = "Error.HttpDataError";
                                break;
                            case DownloadError.InsufficientSpace:
                                downloadFile.StatusDetails = "Error.InsufficientSpace";
                                break;
                            case DownloadError.TooManyRedirects:
                                downloadFile.StatusDetails = "Error.TooManyRedirects";
                                break;
                            case DownloadError.UnhandledHttpCode:
                                downloadFile.StatusDetails = "Error.UnhandledHttpCode";
                                break;
                            case DownloadError.Unknown:
                                downloadFile.StatusDetails = "Error.Unknown";
                                break;
                            default:
                                downloadFile.StatusDetails = "Error.Unregistered: " + reasonFailed;
                                break;
                        }
                    }
                    downloadFile.Status = DownloadFileStatus.FAILED;
                    RemoveFile(downloadFile);
                    //CrossLocalNotifications_Droid.Current.PushNotify(downloadFile.NameFile, "Tải tập tin không thành công", null);
                    break;

                case DownloadStatus.Paused:
                    var reasonPaused = cursor.GetInt(cursor.GetColumnIndex(Android.App.DownloadManager.ColumnReason));
                    switch ((DownloadPausedReason)reasonPaused)
                    {
                        case DownloadPausedReason.QueuedForWifi:
                            downloadFile.StatusDetails = "Paused.QueuedForWifi";
                            break;
                        case DownloadPausedReason.WaitingToRetry:
                            downloadFile.StatusDetails = "Paused.WaitingToRetry";
                            break;
                        case DownloadPausedReason.WaitingForNetwork:
                            downloadFile.StatusDetails = "Paused.WaitingForNetwork";
                            break;
                        case DownloadPausedReason.Unknown:
                            downloadFile.StatusDetails = "Paused.Unknown";
                            break;
                        default:
                            downloadFile.StatusDetails = "Paused.Unregistered: " + reasonPaused;
                            break;
                    }
                    downloadFile.Status = DownloadFileStatus.PAUSED;
                    break;

                case DownloadStatus.Pending:
                    downloadFile.StatusDetails = default(string);
                    downloadFile.Status = DownloadFileStatus.PENDING;
                    break;

                case DownloadStatus.Running:
                    downloadFile.StatusDetails = default(string);
                    downloadFile.Status = DownloadFileStatus.RUNNING;
                    break;
            }
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
            StopDownloadWatcher();
        }
    }
}