using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Plugin.DownloadFiles.Abstractions
{
    public interface IDownloadManager
    {
        /// <summary>
        /// Gets the queue holding all the pending and downloading files.
        /// </summary>
        /// <value>The queue.</value>
        IEnumerable<IDownloadFile> Queue { get; }

        /// <summary>
        /// A function, returning the name of the path, where the download-file, given as argument, should be saved.
        /// </summary>
        /// <value>The path name for downloaded file.</value>
        Func<IDownloadFile, string> PathNameForDownloadedFile { get; set; }

        /// <summary>
        /// Creates a download file.
        /// </summary>
        /// <returns>The download file.</returns>
        /// <param name="url">URL to download.</param>
        IDownloadFile CreateDownloadFile(string url);

        /// <summary>
        /// Creates a download file.
        /// </summary>
        /// <returns>The download file.</returns>
        /// <param name="url">URL to download.</param>
        /// <param name="headers">Headers to send along when requesting the URL.</param>
        IDownloadFile CreateDownloadFile(string url, IDictionary<string, string> headers);

        /// <summary>
        /// Start downloading the file. Most of the systems will put this file into a queue first.
        /// </summary>
        /// <param name="file">File.</param>
        /// <param name="mobileNetworkAllowed">If mobile network is allowed.</param>
        void Start(IDownloadFile file, bool mobileNetworkAllowed = true);
    }
}
