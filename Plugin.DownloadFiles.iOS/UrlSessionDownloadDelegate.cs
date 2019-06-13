using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Foundation;
using Plugin.DownloadFiles.Abstractions;
using UIKit;

namespace Plugin.DownloadFiles
{
    public class UrlSessionDownloadDelegate : NSUrlSessionDownloadDelegate
    {
        public DownloadManagerImplementation Controller;

        protected DownloadFileImplementation GetDownloadFileByTask(NSUrlSessionTask downloadTask)
        {
            return Controller.Queue
                .Cast<DownloadFileImplementation>()
                .FirstOrDefault(
                    i => i.Task != null &&
                    (int)i.Task.TaskIdentifier == (int)downloadTask.TaskIdentifier
                );
        }

        /**
         * A Task was resumed (or started ..)
         */
        public override void DidResume(NSUrlSession session, NSUrlSessionDownloadTask downloadTask, long resumeFileOffset, long expectedTotalBytes)
        {
            var file = GetDownloadFileByTask(downloadTask);
            if (file == null)
            {
                downloadTask.Cancel();
                return;
            }

            file.Status = DownloadFileStatus.RUNNING;
        }

        public override void DidCompleteWithError(NSUrlSession session, NSUrlSessionTask task, NSError error)
        {
            var file = GetDownloadFileByTask(task);
            if (file == null)
                return;

            if (error == null)
                return;

            file.StatusDetails = error.LocalizedDescription;
            file.Status = DownloadFileStatus.FAILED;

            Controller.RemoveFile(file);
        }

        /**
         * The Task keeps receiving data. Keep track of the current progress ...
         */
        public override void DidWriteData(NSUrlSession session, NSUrlSessionDownloadTask downloadTask, long bytesWritten, long totalBytesWritten, long totalBytesExpectedToWrite)
        {
            var file = GetDownloadFileByTask(downloadTask);
            if (file == null)
            {
                downloadTask.Cancel();
                return;
            }

            file.Status = DownloadFileStatus.RUNNING;

            file.TotalBytesExpected = totalBytesExpectedToWrite;
            file.TotalBytesWritten = totalBytesWritten;
        }

        public override void DidFinishDownloading(NSUrlSession session, NSUrlSessionDownloadTask downloadTask, NSUrl location)
        {
            Console.WriteLine("File downloaded in : {0}", location);
            var file = GetDownloadFileByTask(downloadTask);
            if (file == null)
            {
                downloadTask.Cancel();
                return;
            }

            // On iOS 9 and later, this method is called even so the response-code is 400 or higher. See https://github.com/cocos2d/cocos2d-x/pull/14683
            var response = downloadTask.Response as NSHttpUrlResponse;
            if (response != null && response.StatusCode >= 400)
            {
                file.StatusDetails = "Error.HttpCode: " + response.StatusCode;
                file.Status = DownloadFileStatus.FAILED;

                Controller.RemoveFile(file);
                return;
            }

            var success = true;
            var destinationPathName = Controller.PathNameForDownloadedFile?.Invoke(file);
            if (destinationPathName != null)
            {
                success = MoveDownloadedFile(file, location, destinationPathName);
            }
            else
            {
                file.DestinationPathName = location.ToString();
            }

            // If the file destination is unknown or was moved successfully ...
            if (success)
            {
                file.Status = DownloadFileStatus.COMPLETED;
                string body = "Tải thành công";
                IDictionary<string, string> keyValues = null;
                keyValues = new ConcurrentDictionary<string, string>();
                keyValues.Add(DownloadManagerImplementation.DownloadFile_NotifyKey, file.DestinationPathName);
                //CrossLocalNotifications_iOS.Current.PushNotify(file.NameFile, body, keyValues);
            }
            else
            {
                string body = "Tải không thành công";
                //CrossLocalNotifications_iOS.Current.PushNotify(file.NameFile, body, null);
            }
            Controller.RemoveFile(file);

            ((DownloadManagerImplementation)CrossDownloadManager.Current).StartDownloadManager();
        }

        /**
         * Move the downloaded file to it's destination
         */
        public bool MoveDownloadedFile(DownloadFileImplementation file, NSUrl location, string destinationPathName)
        {
            var fileManager = NSFileManager.DefaultManager;
            NSError removeCopy;
            NSError errorCopy;
            bool success = false;

            string nameFile = destinationPathName.Split('/').Last();
            var URLs = fileManager.GetUrls(NSSearchPathDirectory.DocumentDirectory, NSSearchPathDomain.User);
            NSUrl documentsDictionry = URLs.First();
            NSUrl destinationURL = documentsDictionry.Append(nameFile, false);

            try
            {
                fileManager.Remove(destinationURL, out removeCopy);
                success = fileManager.Copy(location, destinationURL, out errorCopy);
                if (!success)
                {
                    file.StatusDetails = errorCopy.LocalizedDescription;
                    file.Status = DownloadFileStatus.FAILED;
                }
                else
                    file.DestinationPathName = destinationURL.Path;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debugger.Break();
            }

            return success;
        }

        public override void DidFinishEventsForBackgroundSession(NSUrlSession session)
        {
            var handler = CrossDownloadManager.BackgroundSessionCompletionHandler;
            if (handler != null)
            {
                CrossDownloadManager.BackgroundSessionCompletionHandler = null;
                handler();
            }
        }
    }
}