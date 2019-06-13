using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.Net;
using System.Collections.Specialized;
using Plugin.DownloadFiles.Abstractions;

namespace Plugin.DownloadFiles
{
    public class DownloadFileImplementation : IDownloadFile
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public long Id;

        public string Url { get; set; }
        public string MimeType { get; set; }
        public string NameFile { get; set; }
        public string DestinationPathName { get; set; }
        public IDictionary<string, string> Headers { get; }
        public string StatusDetails { get; set; }

        private DownloadFileStatus _status;
        public DownloadFileStatus Status
        {
            get
            {
                return _status;
            }
            set
            {
                if (Equals(_status, value)) return;
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }

        private float _totalBytesExpected;
        public float TotalBytesExpected
        {
            get
            {
                return _totalBytesExpected;
            }
            set
            {
                if (Equals(_totalBytesExpected, value)) return;
                _totalBytesExpected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalBytesExpected)));
            }
        }

        private float _totalBytesWritten;
        public float TotalBytesWritten
        {
            get
            {
                return _totalBytesWritten;
            }
            set
            {
                if (Equals(_totalBytesWritten, value)) return;
                _totalBytesWritten = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalBytesWritten)));
            }
        }
        private int _totalRequestException = 0;
        public int TotalRequestException
        {
            get
            {
                return _totalRequestException;
            }
            set
            {
                if (Equals(_totalRequestException, value)) return;
                _totalRequestException = value;
            }
        }

        /**
         * Initializing a new object to add it to the download-queue
         */
        public DownloadFileImplementation(string url, IDictionary<string, string> headers)
        {
            NameFile = url.Split("/").Last();
            Url = url.Replace(" ", "%20");
            Headers = headers;
            //Status = DownloadFileStatus.INITIALIZED;
        }

        /**
         * Reinitializing an object after the app restarted
         */
        public DownloadFileImplementation(ICursor cursor)
        {
            Id = cursor.GetLong(cursor.GetColumnIndex(Android.App.DownloadManager.ColumnId));
            Url = cursor.GetString(cursor.GetColumnIndex(Android.App.DownloadManager.ColumnUri));

            switch ((DownloadStatus)cursor.GetInt(cursor.GetColumnIndex(Android.App.DownloadManager.ColumnStatus)))
            {
                case DownloadStatus.Failed:
                    Status = DownloadFileStatus.FAILED;
                    break;

                case DownloadStatus.Paused:
                    Status = DownloadFileStatus.PAUSED;
                    break;

                case DownloadStatus.Pending:
                    Status = DownloadFileStatus.PENDING;
                    break;

                case DownloadStatus.Running:
                    Status = DownloadFileStatus.RUNNING;
                    break;

                case DownloadStatus.Successful:
                    Status = DownloadFileStatus.COMPLETED;
                    break;
            }
        }

        public void StartDownload(Android.App.DownloadManager downloadManager, string destinationPathName,
            bool allowedOverMetered, DownloadVisibility notificationVisibility, bool isVisibleInDownloadsUi)
        {
            using (var downloadUrl = Uri.Parse(Url))
            using (var request = new Android.App.DownloadManager.Request(downloadUrl))
            {
                if (Headers != null)
                {
                    foreach (var header in Headers)
                    {
                        request.AddRequestHeader(header.Key, header.Value);
                    }
                }

                if (destinationPathName != null)
                {
                    var file = new Java.IO.File(destinationPathName);
                    var uriPathFile = Android.Net.Uri.FromFile(file);
                    request.SetDestinationUri(uriPathFile);
                    //if (file.Exists())
                    //{
                    //    file.Delete();
                    //}
                }
                request.SetVisibleInDownloadsUi(isVisibleInDownloadsUi);
                request.SetAllowedOverMetered(allowedOverMetered);
                request.SetNotificationVisibility(notificationVisibility);
                Id = downloadManager.Enqueue(request);
            }
        }
    }
}