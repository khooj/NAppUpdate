using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NAppUpdate.Framework.Utils;
using NAppUpdate.Framework.Common;

//taken from wyUploader
namespace NAppUpdate.Framework.Sources
{
	/// <summary>Downloads and resumes files from HTTP, HTTPS, FTP, and File (file://) URLS</summary>
	public class ResumableUriSource : IUpdateSource
	{
		// Block size to download is by default 4K.
		const int BufferSize = 4096;

		/// <summary>
		/// This is the name of the file we get back from the server when we
		/// try to download the provided url. It will only contain a non-null
		/// string when we've successfully contacted the server and it has started
		/// sending us a file.
		/// </summary>
		public string DownloadingTo { get; private set; }

		//used to measure download speed
		readonly Stopwatch sw = new Stopwatch();
		long sentSinceLastCalc;
		string downloadSpeed;

		//download site and destination
		private string url_;
		public string FeedUrl { get; private set; }

		// Adler verification
		public long Adler32;
		readonly Adler32 downloadedAdler32 = new Adler32();

		// Signed hash verification
		public byte[] SignedSHA1Hash;
		public string PublicSignKey;

		public bool UseRelativeProgress;

		public static WebProxy CustomProxy;
		private Action<UpdateProgressInfo> _onProgress;
		private string _tempDirectory;
		private ICredentials _credentials;

		public ResumableUriSource(string feedUrl)
		{
			FeedUrl = feedUrl;
			_tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		}

		public ResumableUriSource()
		{

		}

		public string GetUpdatesFeed()
		{
			var data = string.Empty;
			var f = string.Empty;

			GetData(FeedUrl, null, null, ref f);

			using (FileStream fs = File.Open(f, FileMode.Open))
			using (var s = new StreamReader(fs, true))
			{
				data = s.ReadToEnd();
			}

			return data;
		}

		public bool GetData(string url, string baseUrl, Action<UpdateProgressInfo> onProgress, ref string tempLocation)
		{
			try
			{
				return _GetData(url, baseUrl, onProgress, ref tempLocation);
			}
			catch (WebException)
			{
				return _GetData(url, FeedUrl, onProgress, ref tempLocation);
			}
		}

		private bool _GetData(string url, string baseUrl, Action<UpdateProgressInfo> onProgress, ref string tempLocation)
		{
			if (string.IsNullOrEmpty(url))
			{
				onProgress?.Invoke(new DownloadProgressInfo
				{
					Message = "No download urls are specified",
					StillWorking = false,
				});


				return false;
			}

			try
			{
				url = SanitizeUrl(url, baseUrl);
			}
			catch (SystemException)
			{
				//probably something gone wrong, trying to use feedUrl instead
				//see GetData()
				throw new WebException();
			}


			// use the custom proxy if provided
			if (CustomProxy != null)
				WebRequest.DefaultWebProxy = CustomProxy;
			else
				WebRequest.DefaultWebProxy = null;

			Exception ex = null;
			_onProgress = onProgress;
			try
			{
				url_ = url;
				BeginDownload();
				ValidateDownload();
			}
			catch (WebException e)
			{
				throw e;
			}
			catch (Exception except)
			{
				ex = except;
			}

			_onProgress?.Invoke(new DownloadProgressInfo
			{
				Message = ex?.ToString() ?? "",
				StillWorking = false,
			});

			if (ex != null)
				throw ex;

			tempLocation = DownloadingTo;
			return ex == null;
		}

		private string SanitizeUrl(string url, string baseUrl)
		{
			if (baseUrl != null && !baseUrl.EndsWith("/"))
			{
				// assume that files always ends with extension
				// so we can reliable cut out upper directory
				int idx = baseUrl.LastIndexOf('/');
				if (baseUrl.Substring(idx).Contains("."))
					baseUrl = baseUrl.Substring(0, idx + 1);
				else
					baseUrl = baseUrl?.Insert(baseUrl.Length, "/");
			}
			Uri origUrl = null;
			if (!Uri.TryCreate(url, UriKind.Absolute, out origUrl))
			{
				// probably we have filename in url, trying to use baseUrl
				Uri _baseUrl = null;
				if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _baseUrl))
					throw new SystemException("Provided incorrect url and base url: " + url + "; " + baseUrl);
				if (!Uri.TryCreate(_baseUrl, url, out origUrl))
					throw new SystemException("Cannot concat url and base url: " + url + "; " + baseUrl);
			}

			return origUrl.ToString();
		}

		public void SetCredentials(ICredentials credentials)
		{
			_credentials = credentials;
		}

		public static void SetupSaneDownloadOptions()
		{
			ServicePointManager.Expect100Continue = false;
		}

		// Begin downloading the file at the specified url, and save it to the given folder.
		private void BeginDownload()
		{
			DownloadData data = null;
			FileStream fs = null;

			try
			{
				//start the stopwatch for speed calc
				sw.Start();

				if (!Directory.Exists(_tempDirectory))
				{
					Directory.CreateDirectory(_tempDirectory);
				}

				data = DownloadData.Create(url_, _tempDirectory, _credentials);

				//reset the adler
				downloadedAdler32.Reset();

				DownloadingTo = data.Filename;

				if (!File.Exists(DownloadingTo))
				{
					// create the file
					fs = File.Open(DownloadingTo, FileMode.Create, FileAccess.Write);
				}
				else
				{
					// read in the existing data to calculate the adler32
					if (Adler32 != 0)
						GetAdler32(DownloadingTo);

					// apend to an existing file (resume)
					fs = File.Open(DownloadingTo, FileMode.Append, FileAccess.Write);
				}

				// create the download buffer
				byte[] buffer = new byte[BufferSize];

				int readCount;

				// update how many bytes have already been read
				sentSinceLastCalc = data.StartPoint; //for BPS calculation

				// Only increment once for each %
				int LastProgress = 0;
				while ((readCount = data.DownloadStream.Read(buffer, 0, BufferSize)) > 0)
				{
					// update total bytes read
					data.StartPoint += readCount;

					// update the adler32 value
					if (Adler32 != 0)
						downloadedAdler32.Update(buffer, 0, readCount);

					// save block to end of file
					fs.Write(buffer, 0, readCount);

					//calculate download speed
					calculateBps(data.StartPoint, data.TotalDownloadSize);

					// send progress info
					if (data.PercentDone > LastProgress)
					{
						_onProgress?.Invoke(new DownloadProgressInfo
						{
							Percentage = data.PercentDone,
							DownloadedInBytes = data.TotalDownloadSize,
						});

						LastProgress = data.PercentDone;
					}
				}
			}
			catch (UriFormatException e)
			{
				throw new Exception(string.Format("Could not parse the URL \"{0}\" - it's either malformed or is an unknown protocol.", url_), e);
			}
			catch (WebException e)
			{
				throw e;
			}
			catch (Exception e)
			{
				if (string.IsNullOrEmpty(DownloadingTo))
					throw new Exception(string.Format("Error trying to save file: {0}", e.Message), e);
				else
					throw new Exception(string.Format("Error trying to save file \"{0}\": {1}", DownloadingTo, e.Message), e);
			}
			finally
			{
				if (data != null)
					data.Close();
				if (fs != null)
					fs.Close();
			}
		}

		void calculateBps(long BytesReceived, long TotalBytes)
		{
			if (sw.Elapsed < TimeSpan.FromSeconds(2))
				return;

			sw.Stop();

			// Calculcate transfer speed.
			long bytes = BytesReceived - sentSinceLastCalc;
			double bps = bytes * 1000.0 / sw.Elapsed.TotalMilliseconds;
			downloadSpeed = BytesToString(BytesReceived, false) + " / " + (TotalBytes == 0 ? "unknown" : BytesToString(TotalBytes, false)) + "   (" + BytesToString(bps, true) + "/sec)";

			// Estimated seconds remaining based on the current transfer speed.
			//secondsRemaining = (int)((e.TotalBytesToReceive - e.BytesReceived) / bps);

			// Restart stopwatch for next second.
			sentSinceLastCalc = BytesReceived;
			sw.Reset();
			sw.Start();
		}

		static readonly string[] units = new[] { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

		/// <summary>Constructs a download speed indicator string.</summary>
		/// <param name="bytes">Bytes per second transfer rate.</param>
		/// <param name="time">Is a time value (e.g. bytes/second)</param>
		/// <returns>String represenation of the transfer rate in bytes/sec, KB/sec, MB/sec, etc.</returns>
		static string BytesToString(double bytes, bool time)
		{
			int i = 0;
			while (bytes >= 0.9 * 1024)
			{
				bytes /= 1024;
				i++;
			}

			// don't show N.NN bytes when *not* dealing with time values (e.g. 8.88 bytes/sec).
			return (time || i > 0) ? string.Format("{0:0.00} {1}", bytes, units[i]) : string.Format("{0} {1}", bytes, units[i]);
		}

		void ValidateDownload()
		{
			if (Adler32 != 0 && Adler32 != downloadedAdler32.Value)
			{
				// file failed to vaildate, throw an error
				throw new Exception("The downloaded file \"" + Path.GetFileName(DownloadingTo) + "\" failed the Adler32 validation.");
			}

			if (PublicSignKey != null)
			{
				if (SignedSHA1Hash == null)
					throw new Exception("The downloaded file \"" + Path.GetFileName(DownloadingTo) + "\" is not signed.");

				byte[] hash = null;

				try
				{
					using (FileStream fs = new FileStream(DownloadingTo, FileMode.Open, FileAccess.Read))
					using (SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider())
					{
						hash = sha1.ComputeHash(fs);
					}

					RSACryptoServiceProvider RSA = new RSACryptoServiceProvider();
					RSA.FromXmlString(PublicSignKey);

					RSAPKCS1SignatureDeformatter RSADeformatter = new RSAPKCS1SignatureDeformatter(RSA);
					RSADeformatter.SetHashAlgorithm("SHA1");

					// verify signed hash
					if (!RSADeformatter.VerifySignature(hash, SignedSHA1Hash))
					{
						// The signature is not valid.
						throw new Exception("Verification failed.");
					}
				}
				catch (Exception ex)
				{
					string msg = "The downloaded file \"" + Path.GetFileName(DownloadingTo) +
									   "\" failed the signature validation: " + ex.Message;

					long sizeInBytes = new FileInfo(DownloadingTo).Length;

					msg += "\r\n\r\nThis error is likely caused by a download that ended prematurely. Total size of the downloaded file: " + BytesToString(sizeInBytes, false);

					// show the size in bytes only if the size displayed isn't already in bytes
					if (sizeInBytes >= 0.9 * 1024)
						msg += " (" + sizeInBytes + " bytes).";

					if (hash != null)
						msg += "\r\n\r\nComputed SHA1 hash of the downloaded file: " + BitConverter.ToString(hash);

					throw new Exception(msg);
				}
			}
		}

		void GetAdler32(string fileName)
		{
			byte[] buffer = new byte[BufferSize];

			using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read))
			{
				int sourceBytes;

				do
				{
					sourceBytes = fs.Read(buffer, 0, buffer.Length);
					downloadedAdler32.Update(buffer, 0, sourceBytes);
				} while (sourceBytes > 0);
			}
		}
	}


	class DownloadData
	{
		WebResponse response;

		Stream stream;
		long size;
		long start;

		public string Filename;

		public WebResponse Response
		{
			get { return response; }
			set { response = value; }
		}

		public Stream DownloadStream
		{
			get
			{
				if (start == size)
					return Stream.Null;

				return stream ?? (stream = response.GetResponseStream());
			}
		}

		public int PercentDone
		{
			get
			{
				if (size > 0)
					return (int)((start * 100) / size);

				return 0;
			}
		}

		public long TotalDownloadSize
		{
			get { return size; }
		}

		public long StartPoint
		{
			get { return start; }
			set { start = value; }
		}

		public bool IsProgressKnown
		{
			get
			{
				// If the size of the remote url is -1, that means we
				// couldn't determine it, and so we don't know
				// progress information.
				return size > -1;
			}
		}

		readonly static List<char> invalidFilenameChars = new List<char>(Path.GetInvalidFileNameChars());

		public static DownloadData Create(string url, string destFolder, ICredentials creds)
		{
			DownloadData downloadData = new DownloadData();
			WebRequest req = GetRequest(url, creds);

			try
			{
				if (req is FtpWebRequest)
				{
					// get the file size for FTP files
					req.Method = WebRequestMethods.Ftp.GetFileSize;
					downloadData.response = req.GetResponse();
					downloadData.GetFileSize();

					// send REST cmd to 0 in case after last file server doesnt cleared it already
					//req = GetRequest(url);
					//downloadData.response = req.GetResponse();

					// new request for downloading the FTP file
					req = GetRequest(url, creds);
					((FtpWebRequest)req).ContentOffset = 0;
					downloadData.response = req.GetResponse();
				}
				else
				{
					downloadData.response = req.GetResponse();
					downloadData.GetFileSize();
				}
			}
			catch (WebException e)
			{
				throw e;
			}
			catch (Exception e)
			{
				throw new Exception(string.Format("Error downloading \"{0}\": {1}", url, e.Message), e);
			}

			// Check to make sure the response isn't an error. If it is this method
			// will throw exceptions.
			ValidateResponse(downloadData.response, url);

			// Take the name of the file given to use from the web server.
			string fileName = downloadData.response.Headers["Content-Disposition"];

			if (fileName != null)
			{
				int fileLoc = fileName.IndexOf("filename=", StringComparison.OrdinalIgnoreCase);

				if (fileLoc != -1)
				{
					// go past "filename="
					fileLoc += 9;

					if (fileName.Length > fileLoc)
					{
						// trim off an ending semicolon if it exists
						int end = fileName.IndexOf(';', fileLoc);

						if (end == -1)
							end = fileName.Length - fileLoc;
						else
							end -= fileLoc;

						fileName = fileName.Substring(fileLoc, end).Trim();
					}
					else
						fileName = null;
				}
				else
					fileName = null;
			}

			if (string.IsNullOrEmpty(fileName))
			{
				// brute force the filename from the url
				fileName = Path.GetFileName(downloadData.response.ResponseUri.LocalPath);
			}

			// trim out non-standard filename characters
			if (!string.IsNullOrEmpty(fileName) && fileName.IndexOfAny(invalidFilenameChars.ToArray()) != -1)
			{
				//make a new string builder (with at least one bad character)
				StringBuilder newText = new StringBuilder(fileName.Length - 1);

				//remove the bad characters
				for (int i = 0; i < fileName.Length; i++)
				{
					if (invalidFilenameChars.IndexOf(fileName[i]) == -1)
						newText.Append(fileName[i]);
				}

				fileName = newText.ToString().Trim();
			}

			// if filename *still* is null or empty, then generate some random temp filename
			if (string.IsNullOrEmpty(fileName))
				fileName = Path.GetFileName(Path.GetTempFileName());

			string downloadTo = Path.Combine(destFolder, fileName);

			downloadData.Filename = downloadTo;

			// If we don't know how big the file is supposed to be,
			// we can't resume, so delete what we already have if something is on disk already.
			if (!downloadData.IsProgressKnown && File.Exists(downloadTo))
				File.Delete(downloadTo);

			if (downloadData.IsProgressKnown && File.Exists(downloadTo))
			{
				// Try and start where the file on disk left off
				downloadData.start = new FileInfo(downloadTo).Length;

				// If we have a file that's bigger than what is online, then something 
				// strange happened. Delete it and start again.
				if (downloadData.start > downloadData.size)
					File.Delete(downloadTo);
				else if (downloadData.start < downloadData.size)
				{
					// Try and resume by creating a new request with a new start position
					downloadData.response.Close();

					if (downloadData.response is HttpWebResponse)
					{
						req = GetRequest(url, creds);
						((HttpWebRequest)req).AddRange((int)downloadData.start);
						downloadData.response = req.GetResponse();

						if (((HttpWebResponse)downloadData.Response).StatusCode != HttpStatusCode.PartialContent)
						{
							// They didn't support our resume request. 
							File.Delete(downloadTo);
							downloadData.start = 0;
						}
					}
					else
					{
						req = GetRequest(url, creds);
						((FtpWebRequest)req).ContentOffset = (int)downloadData.start;
						downloadData.response = req.GetResponse();

						if ((downloadData.Response as FtpWebResponse).StatusCode != FtpStatusCode.FileCommandPending)
						{
							File.Delete(downloadTo);
							downloadData.start = 0;
						}
					}
				}
			}

			return downloadData;
		}

		// Checks whether a WebResponse is an error.
		static void ValidateResponse(WebResponse response, string url)
		{
			if (response is HttpWebResponse)
			{
				HttpWebResponse httpResponse = (HttpWebResponse)response;

				// If it's an HTML page, it's probably an error page.
				if (httpResponse.StatusCode == HttpStatusCode.NotFound || httpResponse.ContentType.Contains("text/html"))
				{
					throw new Exception(
						string.Format("Could not download \"{0}\" - a web page was returned from the web server.", url));
				}
			}
			else if (response is FtpWebResponse)
			{
				if (((FtpWebResponse)response).StatusCode == FtpStatusCode.ConnectionClosed)
					throw new Exception(string.Format("Could not download \"{0}\" - FTP server closed the connection.", url));
			}
			// FileWebResponse doesn't have a status code to check.
		}

		void GetFileSize()
		{
			if (response != null)
			{
				try
				{
					size = response.ContentLength;
				}
				catch
				{
					//file size couldn't be determined
					size = -1;
				}
			}
		}

		static WebRequest GetRequest(string url, ICredentials creds)
		{
			UriBuilder uri = new UriBuilder(url);
			bool hasCredentials = !string.IsNullOrEmpty(uri.UserName) && !string.IsNullOrEmpty(uri.Password) || creds != null;
			if (hasCredentials && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
			{
				// get the URL without user/password
				url = (new UriBuilder(uri.Scheme, uri.Host, uri.Port, uri.Path, uri.Fragment)).ToString();
			}

			WebRequest request = WebRequest.Create(url);
			request.Timeout = 5 * 1000; //msec
			ICredentials tmp;
			if (hasCredentials)
			{
				if (creds != null)
					tmp = creds;
				else
					tmp = new NetworkCredential(uri.UserName, uri.Password);
			}
			else
			{
				if (request is HttpWebRequest)
					tmp = CredentialCache.DefaultCredentials;
				else
					tmp = null;
			}
			if (tmp != null)
				request.Credentials = tmp;

			if (request is HttpWebRequest)
			{
				// Some servers explode if the user agent is missing.
				// Some servers explode if the user agent is "non-standard" (e.g. "wyUpdate / " + VersionTools.FromExecutingAssembly())

				// Thus we're forced to mimic IE 9 User agent
				((HttpWebRequest)request).UserAgent = "Mozilla/5.0 (Windows; U; MSIE 9.0; Windows NT 6.1; en-US; NAppUpdate)";
			}
			else if (request is FtpWebRequest)
			{
				// set to binary mode (should fix crummy servers that need this spelled out to them)
				// namely ProFTPd that chokes if you request the file size without first setting "TYPE I" (binary mode)
				(request as FtpWebRequest).UseBinary = true;
			}

			return request;
		}

		public void Close()
		{
			response.Close();
		}
	}
}
