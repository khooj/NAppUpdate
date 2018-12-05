using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.IO;
using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using Newtonsoft.Json;

namespace feedbuildercon
{
	class StartStopOption
	{
		public string Operation { get; set; }
		public string When { get; set; } // "at_start", "at_end", "before", "after"
		public string Target { get; set; }
		public string Executable { get; set; }
	}

	class Options
	{
		public string MachineName { get; set; } = string.Empty;
		public string BaseURL { get; set; } = string.Empty;
		public bool CompareVersion { get; set; } = false;
		public string OutputFolder { get; set; } = string.Empty;
		public string AddExtension { get; set; } = string.Empty;
		public bool CopyFiles { get; set; } = false;
		public bool CompareHash { get; set; } = false;
		public bool CompareSize { get; set; } = false;
		public bool CleanUp { get; set; } = false;
		public bool CompareDate { get; set; } = false;
		public bool IgnoreDebugSymbols { get; set; } = false;
		public IList<string> IgnoreFiles { get; set; }
		public IList<StartStopOption> LaunchFiles { get; set; }
		public string FeedXML { get; set; } = string.Empty;
		public bool IgnoreVsHosting { get; set; } = false;

		public static string Serialize(Options opts)
		{
			string val = JsonConvert.SerializeObject(opts, new JsonSerializerSettings
			{
				DefaultValueHandling = DefaultValueHandling.Ignore,
				Formatting = Newtonsoft.Json.Formatting.Indented
			});
			return val;
		}

		public static Options Deserialize(string json)
		{
			Options opts = JsonConvert.DeserializeObject<Options>(json);
			return opts == null ? new Options() : opts;
		}
	}

	public class Main
	{
		private string FileName;
		private Options _options;
		private IList<FileInfo> _files;
		private ArgumentsParser _argParser;

		public Main(IEnumerable<string> args)
		{
			_argParser = new ArgumentsParser(args);
			if (!_argParser.HasArgs)
			{
				PrintHelp();
				return;
			}

			FileName = _argParser.FileName;
			if (_argParser.Example)
			{
				Example();
				return;
			}

			if (!string.IsNullOrEmpty(FileName))
			{
				if (File.Exists(FileName))
				{
					LoadJson(FileName);
					CheckOptions();
					PrepareFiles();
				}
				else
				{
					throw new ArgumentException("File not exists");
				}
			}
			if (_argParser.Build) Build();
		}

		private void PrintHelp()
		{
			StringBuilder s = new StringBuilder();
			s.AppendFormat("Usage: feedbuildercon <cmd> <config>\n");
			s.AppendFormat("cmd - build, example\n");
			s.AppendFormat("config - path to config file\n");
			Console.WriteLine(s.ToString());
		}

		private void CheckOptions()
		{
			if (string.IsNullOrEmpty(_options.OutputFolder))
			{
				_options.OutputFolder = string.Empty;
			}
			else
			{
				string path = GetFullDirectoryPath(_options.OutputFolder);
				if (!Directory.Exists(path))
					throw new ArgumentException("OutputFolder directory not exists");
			}

			if (_options.IgnoreFiles == null)
				_options.IgnoreFiles = new List<string>();

			if (_options.LaunchFiles == null)
				_options.LaunchFiles = new List<StartStopOption>();

			if (string.IsNullOrEmpty(_options.FeedXML))
			{
				throw new ArgumentException("FeedXML path should not be empty");
			}

			if (string.IsNullOrEmpty(_options.BaseURL))
				_options.BaseURL = string.Empty;
		}

		private void PrepareFiles()
		{
			if (_files == null)
				_files = new List<FileInfo>();

			string outputDir = GetFullDirectoryPath(_options.OutputFolder);

			//should just throw unauthorized access exception and others
			_files = GetFilesInDirectory(outputDir);
		}

		private IList<FileInfo> GetFilesInDirectory(string path)
		{
			IList<FileInfo> lst = new List<FileInfo>();
			foreach (string filename in Directory.GetFiles(path))
			{
				lst.Add(new FileInfo(filename));
			}

			foreach (string dirname in Directory.GetDirectories(path))
				foreach (FileInfo f in GetFilesInDirectory(dirname))
					lst.Add(f);

			return lst;
		}

		private string GetFullDirectoryPath(string path)
		{
			string absolutePath = path;

			if (!absolutePath.EndsWith("\\"))
			{
				absolutePath += "\\";
			}

			return Path.GetFullPath(absolutePath);
		}

		private void SaveJson(string filePath)
		{
			_options.MachineName = Environment.MachineName;
			string val = Options.Serialize(_options);
			using (FileStream fs = File.Open(filePath, FileMode.Create))
			using (StreamWriter sw = new StreamWriter(fs))
				sw.Write(val);
		}

		private void LoadJson(string filePath)
		{
			string val = string.Empty;
			using (FileStream fs = File.Open(filePath, FileMode.Open))
			using (StreamReader sr = new StreamReader(fs))
				val = sr.ReadToEnd();
			_options = Options.Deserialize(val);
		}

		private void ResetJson()
		{
			_options = Options.Deserialize(string.Empty);
		}

		private void Build()
		{
			string baseUrl = _options.BaseURL.Trim();
			Console.WriteLine("Building NAppUpdater feed '{0}'", baseUrl);

			// If the target folder doesn't exist, create a path to it
			string dest = _options.FeedXML.Trim();
			var destDir = Directory.GetParent(GetFullDirectoryPath(Path.GetDirectoryName(dest)));
			if (!Directory.Exists(destDir.FullName)) Directory.CreateDirectory(destDir.FullName);

			XmlDocument doc = new XmlDocument();
			XmlDeclaration dec = doc.CreateXmlDeclaration("1.0", "utf-8", null);

			doc.AppendChild(dec);
			XmlElement feed = doc.CreateElement("Feed");
			if (!string.IsNullOrEmpty(baseUrl)) feed.SetAttribute("BaseUrl", baseUrl);
			doc.AppendChild(feed);

			XmlElement tasks = doc.CreateElement("Tasks");

			Dictionary<string, IList<StartStopOption>> startStopProcesses = null;
			List<StartStopOption> atEnd = null;
			if (_options.LaunchFiles != null)
			{
				startStopProcesses = new Dictionary<string, IList<StartStopOption>>();

				foreach (StartStopOption s in _options.LaunchFiles)
				{
					if (s.When == "at_start")
					{
						tasks.AppendChild(CreateXmlStartStopTask(s, doc));
						continue;
					}

					if (s.When == "at_end")
					{
						if (atEnd == null)
							atEnd = new List<StartStopOption>();
						atEnd.Add(s);
						continue;
					}

					if (startStopProcesses.ContainsKey(s.Target))
						startStopProcesses[s.Target].Add(s);
					else
						startStopProcesses.Add(s.Target, new List<StartStopOption>() { s });
				}
			}

			Console.WriteLine("Processing feed items");
			int itemsCopied = 0;
			int itemsCleaned = 0;
			int itemsSkipped = 0;
			int itemsFailed = 0;
			int itemsMissingConditions = 0;
			foreach (FileInfo thisItem in _files)
			{
				if (_options.IgnoreFiles != null && _options.IgnoreFiles.Contains(thisItem.Name))
				{
					continue;
				}

				string destFile = "";
				string filename = "";
				try
				{
					filename = thisItem.Name;
					destFile = Path.Combine(destDir.FullName, filename);
				}
				catch { }
				if (destFile == "" || filename == "")
				{
					throw new ArgumentException(string.Format("The file could not be pathed:\nFolder:'{0}'\nFile:{1}", destDir.FullName, filename));
				}

				IList<StartStopOption> startStopOptions = null;
				if (startStopProcesses != null && startStopProcesses.ContainsKey(thisItem.Name))
					startStopOptions = startStopProcesses[thisItem.Name];

				XmlElement task = doc.CreateElement("FileUpdateTask");
				task.SetAttribute("localPath", thisItem.Name);
				// generate FileUpdateTask metadata items
				task.SetAttribute("lastModified", thisItem.LastWriteTime.ToFileTime().ToString(CultureInfo.InvariantCulture));
				if (!string.IsNullOrEmpty(_options.AddExtension))
				{
					task.SetAttribute("updateTo", AddExtensionToPath(thisItem.Name, _options.AddExtension));
				}

				var fileVersionInfo = FileVersionInfo.GetVersionInfo(thisItem.FullName);
				string fileVersion = new Version(
					fileVersionInfo.FileMajorPart,
					fileVersionInfo.FileMinorPart,
					fileVersionInfo.FileBuildPart,
					fileVersionInfo.FilePrivatePart).ToString();

				task.SetAttribute("fileSize", thisItem.Length.ToString(CultureInfo.InvariantCulture));
				if (!string.IsNullOrEmpty(fileVersion))
					task.SetAttribute("version", fileVersion);

				XmlElement conds = doc.CreateElement("Conditions");
				XmlElement cond;

				//File Exists
				cond = doc.CreateElement("FileExistsCondition");
				cond.SetAttribute("type", "or-not");
				conds.AppendChild(cond);

				//Version
				if (_options.CompareVersion && !string.IsNullOrEmpty(fileVersion))
				{
					cond = doc.CreateElement("FileVersionCondition");
					cond.SetAttribute("type", "or");
					cond.SetAttribute("what", "below");
					cond.SetAttribute("version", fileVersion);
					conds.AppendChild(cond);
				}

				//Size
				if (_options.CompareSize)
				{
					cond = doc.CreateElement("FileSizeCondition");
					cond.SetAttribute("type", "or-not");
					cond.SetAttribute("what", "is");
					cond.SetAttribute("size", thisItem.Length.ToString(CultureInfo.InvariantCulture));
					conds.AppendChild(cond);
				}

				//Date
				if (_options.CompareDate)
				{
					cond = doc.CreateElement("FileDateCondition");
					cond.SetAttribute("type", "or");
					cond.SetAttribute("what", "older");
					// local timestamp, not UTC
					cond.SetAttribute("timestamp", thisItem.LastWriteTime.ToFileTime().ToString(CultureInfo.InvariantCulture));
					conds.AppendChild(cond);
				}

				//Hash
				if (_options.CompareHash)
				{
					cond = doc.CreateElement("FileChecksumCondition");
					cond.SetAttribute("type", "or-not");
					cond.SetAttribute("checksumType", "sha256");
					cond.SetAttribute("checksum", GetSHA256Checksum(thisItem.FullName));
					conds.AppendChild(cond);
				}

				if (conds.ChildNodes.Count == 0) itemsMissingConditions++;
				task.AppendChild(conds);

				if (startStopOptions != null)
					foreach (StartStopOption s in startStopOptions)
						if (s.When == "before")
							tasks.AppendChild(CreateXmlStartStopTask(s, doc));

				tasks.AppendChild(task);

				if (startStopOptions != null)
					foreach (StartStopOption s in startStopOptions)
						if (s.When == "after")
							tasks.AppendChild(CreateXmlStartStopTask(s, doc));

				if (_options.CopyFiles)
				{
					if (CopyFile(thisItem.FullName, destFile))
						itemsCopied++;
					else
						itemsFailed++;
				}
			}

			if (atEnd != null)
				foreach (StartStopOption s in atEnd)
					tasks.AppendChild(CreateXmlStartStopTask(s, doc));

			feed.AppendChild(tasks);

			string xmlDest = Path.Combine(destDir.FullName, Path.GetFileName(dest));
			doc.Save(xmlDest);

			Console.WriteLine("Done building feed.");
			if (itemsCopied > 0) Console.WriteLine("{0,5} items copied", itemsCopied);
			if (itemsCleaned > 0) Console.WriteLine("{0,5} items cleaned", itemsCleaned);
			if (itemsSkipped > 0) Console.WriteLine("{0,5} items skipped", itemsSkipped);
			if (itemsFailed > 0) Console.WriteLine("{0,5} items failed", itemsFailed);
			if (itemsMissingConditions > 0) Console.WriteLine("{0,5} items without any conditions", itemsMissingConditions);
		}

		private bool CopyFile(string sourceFile, string destFile)
		{
			// If the target folder doesn't exist, create the path to it
			var fi = new FileInfo(destFile);
			var d = Directory.GetParent(fi.FullName);
			if (!Directory.Exists(d.FullName))
				CreateDirectoryPath(d.FullName);
			if (!string.IsNullOrEmpty(_options.AddExtension))
			{
				destFile = AddExtensionToPath(destFile, _options.AddExtension);
			}

			// Copy with delayed retry
			int retries = 3;
			while (retries > 0)
			{
				try
				{
					if (File.Exists(destFile)) File.Delete(destFile);
					File.Copy(sourceFile, destFile);
					retries = 0; // success
					return true;
				}
				catch (IOException)
				{
					// Failed... let's try sleeping a bit (slow disk maybe)
					if (retries-- > 0) Thread.Sleep(200);
				}
				catch (UnauthorizedAccessException)
				{
					// same handling as IOException
					if (retries-- > 0) Thread.Sleep(200);
				}
			}
			return false;
		}

		private string AddExtensionToPath(string filePath, string extension)
		{
			string sanitizedExtension = (extension.Trim().StartsWith(".") ? String.Empty : ".") + extension.Trim();
			return filePath + sanitizedExtension;
		}

		private void CreateDirectoryPath(string directoryPath)
		{
			// Create the folder/path if it doesn't exist, with delayed retry
			int retries = 3;
			while (retries > 0 && !Directory.Exists(directoryPath))
			{
				Directory.CreateDirectory(directoryPath);
				if (retries-- < 3) Thread.Sleep(200);
			}
		}

		public static string GetSHA256Checksum(string filePath)
		{
			using (FileStream stream = File.OpenRead(filePath))
			{
				SHA256Managed sha = new SHA256Managed();
				byte[] checksum = sha.ComputeHash(stream);
				return BitConverter.ToString(checksum).Replace("-", string.Empty);
			}
		}

		private XmlElement CreateXmlStartStopTask(StartStopOption opt, XmlDocument doc)
		{
			XmlElement task = null;
			switch (opt.Operation)
			{
				case "start":
					task = doc.CreateElement("StartProcessTask");
					break;
				case "stop":
					task = doc.CreateElement("StopProcessTask");
					break;
				default:
					throw new ArgumentException("Wrong value in Operation section: " + JsonConvert.SerializeObject(opt));
			}

			if (string.IsNullOrEmpty(opt.Executable))
				throw new ArgumentException("Wrong value in Executable section: " + JsonConvert.SerializeObject(opt));

			task.SetAttribute("name", opt.Executable);
			task.SetAttribute("shell", "True");
			return task;
		}

		private void Example()
		{
			ResetJson();
			_options.IgnoreFiles = new List<string>();
			_options.LaunchFiles = new List<StartStopOption>()
			{
				new StartStopOption
				{
					Executable = "stop_service.bat",
					Operation = "stop",
					When = "before",
					Target = "service.exe" // will execute before service.exe update
				},
				new StartStopOption
				{
					Executable = "service.exe",
					Operation = "start",
					When = "after",
					Target = "service.exe" // will start after file update
				}
			};
			SaveJson(FileName);
		}
	}
}
