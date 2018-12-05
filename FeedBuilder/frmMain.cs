using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Newtonsoft.Json;

namespace FeedBuilder
{
	public partial class frmMain : Form
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

		public frmMain()
		{
			InitializeComponent();
		}

		#region " Private constants/variables"

		private const string DialogFilter = "Feed configuration files (*.config)|*.config|All files (*.*)|*.*";
		private const string DefaultFileName = "FeedBuilder.config";
		private OpenFileDialog _openDialog;

		#endregion

		private ArgumentsParser _argParser;

		#region " Properties"

		public string FileName { get; set; }
		public bool ShowGui { get; set; }
		private Options _options;

		#endregion

		#region " Loading/Initialization/Lifetime"

		private void frmMain_Load(Object sender, EventArgs e)
		{
			Visible = false;
			ResetJson();
			InitializeFormSettings();
			string[] args = Environment.GetCommandLineArgs();
			// The first arg is the path to ourself
			//If args.Count >= 2 Then
			//    If File.Exists(args(1)) Then
			//        Dim p As New FeedBuilderSettingsProvider()
			//        p.LoadFrom(args(1))
			//        Me.FileName = args(1)
			//    End If
			//End If

			// The first arg is the path to ourself
			_argParser = new ArgumentsParser(args);

			if (!_argParser.HasArgs)
			{
				FreeConsole();
				return;
			}

			FileName = _argParser.FileName;
			if (_argParser.Example)
			{
				Example();
				Close();
			}

			if (!string.IsNullOrEmpty(FileName))
			{
				if (File.Exists(FileName))
				{
					LoadJson(FileName);
					InitializeFormSettings();
				}
				else
				{
					_argParser.ShowGui = true;
					_argParser.Build = false;
					UpdateTitle();
				}
			}
			if (_argParser.ShowGui) Show();
			if (_argParser.Build) Build();
			if (!_argParser.ShowGui) Close();
		}

		private void InitializeFormSettings()
		{
			if (string.IsNullOrEmpty(_options.OutputFolder))
			{
				txtOutputFolder.Text = string.Empty;
			}
			else
			{
				string path = GetFullDirectoryPath(_options.OutputFolder);
				txtOutputFolder.Text = Directory.Exists(path) ? _options.OutputFolder : string.Empty;
			}

			if (_options.IgnoreFiles == null)
				_options.IgnoreFiles = new List<string>();

			if (_options.LaunchFiles == null)
				_options.LaunchFiles = new List<StartStopOption>();

			txtFeedXML.Text = string.IsNullOrEmpty(_options.FeedXML) ? string.Empty : _options.FeedXML;
			txtBaseURL.Text = string.IsNullOrEmpty(_options.BaseURL) ? string.Empty : _options.BaseURL;

			chkVersion.Checked = _options.CompareVersion;
			chkSize.Checked = _options.CompareSize;
			chkDate.Checked = _options.CompareDate;
			chkHash.Checked = _options.CompareHash;

			chkIgnoreSymbols.Checked = _options.IgnoreDebugSymbols;
			chkIgnoreVsHost.Checked = _options.IgnoreVsHosting;
			chkCopyFiles.Checked = _options.CopyFiles;
			chkCleanUp.Checked = _options.CleanUp;
            txtAddExtension.Text = _options.AddExtension;

			// fix access denied error at startup and console usage
			try
			{
				ReadFiles();
			}
			catch (UnauthorizedAccessException e)
			{
				//TODO: move out exception check
				//_options.Reset();
				// form should be empty now
				//InitializeFormSettings(); 
				MessageBox.Show("MOVE OUT EXCEPTION CHECK!!!");
				throw;
			}

			UpdateTitle();
		}

		private void UpdateTitle()
		{
			if (string.IsNullOrEmpty(FileName)) Text = "Feed Builder";
			else Text = "Feed Builder - " + FileName;
		}

		private void SaveFormSettings()
		{
			if (!string.IsNullOrEmpty(txtOutputFolder.Text.Trim()) && Directory.Exists(txtOutputFolder.Text.Trim())) _options.OutputFolder = txtOutputFolder.Text.Trim();
			// ReSharper disable AssignNullToNotNullAttribute
			if (!string.IsNullOrEmpty(txtFeedXML.Text.Trim()) && Directory.Exists(Path.GetDirectoryName(txtFeedXML.Text.Trim()))) _options.FeedXML = txtFeedXML.Text.Trim();
			// ReSharper restore AssignNullToNotNullAttribute
			if (!string.IsNullOrEmpty(txtBaseURL.Text.Trim())) _options.BaseURL = txtBaseURL.Text.Trim();

            if (!string.IsNullOrEmpty(txtAddExtension.Text.Trim())) _options.AddExtension = txtAddExtension.Text.Trim();

			if (_options.IgnoreFiles == null)
				_options.IgnoreFiles = new List<string>();
			_options.IgnoreFiles.Clear();

			if (_options.LaunchFiles == null)
				_options.LaunchFiles = new List<StartStopOption>();

			_options.CompareVersion = chkVersion.Checked;
			_options.CompareSize = chkSize.Checked;
			_options.CompareDate = chkDate.Checked;
			_options.CompareHash = chkHash.Checked;

			_options.IgnoreDebugSymbols = chkIgnoreSymbols.Checked;
			_options.IgnoreVsHosting = chkIgnoreVsHost.Checked;
			_options.CopyFiles = chkCopyFiles.Checked;
			_options.CleanUp = chkCleanUp.Checked;

			foreach (ListViewItem thisItem in lstFiles.Items)
			{
				if (!thisItem.Checked)
					_options.IgnoreFiles.Add(thisItem.Text);
			}
		}

		private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
		{
			SaveFormSettings();
			SaveJson(FileName);
		}

		#endregion

		#region " Commands Events"

		private void cmdBuild_Click(Object sender, EventArgs e)
		{
			Build();
		}

		private void btnOpenOutputs_Click(object sender, EventArgs e)
		{
			OpenOutputsFolder();
		}

		private void btnNew_Click(Object sender, EventArgs e)
		{
			ResetJson();
			InitializeFormSettings();
		}

		private void btnOpen_Click(Object sender, EventArgs e)
		{
			OpenFileDialog dlg;
			if (_openDialog == null)
			{
				dlg = new OpenFileDialog
				{
					CheckFileExists = true,
					FileName = string.IsNullOrEmpty(FileName) ? DefaultFileName : FileName
				};
				_openDialog = dlg;
			}
			else dlg = _openDialog;
			dlg.Filter = DialogFilter;
			if (dlg.ShowDialog() != DialogResult.OK) return;
			LoadJson(dlg.FileName);
			FileName = dlg.FileName;
			InitializeFormSettings();
		}

		private void btnSave_Click(Object sender, EventArgs e)
		{
			Save(false);
		}

		private void btnSaveAs_Click(Object sender, EventArgs e)
		{
			Save(true);
		}

		private void btnRefresh_Click(Object sender, EventArgs e)
		{
			ReadFiles();
		}

		#endregion

		#region " Options Events"

		private void cmdOutputFolder_Click(Object sender, EventArgs e)
		{
			fbdOutputFolder.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			if (fbdOutputFolder.ShowDialog(this) != DialogResult.OK) return;
			txtOutputFolder.Text = fbdOutputFolder.SelectedPath;
			try
			{
				ReadFiles();
			}
			catch (UnauthorizedAccessException ex)
			{
				lstFiles.Items.Clear();
				MessageBox.Show("Cannot open selected folder.");
			}
		}

		private void cmdFeedXML_Click(Object sender, EventArgs e)
		{
			sfdFeedXML.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			if (sfdFeedXML.ShowDialog(this) == DialogResult.OK) txtFeedXML.Text = sfdFeedXML.FileName;
		}

		private void chkIgnoreSymbols_CheckedChanged(object sender, EventArgs e)
		{
			ReadFiles();
		}

		private void chkCopyFiles_CheckedChanged(Object sender, EventArgs e)
		{
			chkCleanUp.Enabled = chkCopyFiles.Checked;
			if (!chkCopyFiles.Checked) chkCleanUp.Checked = false;
		}

		#endregion

		#region " Helper Methods "

		private void SaveJson(string filePath)
		{
			try
			{
				_options.MachineName = Environment.MachineName;
				string val = Options.Serialize(_options);
				using (FileStream fs = File.Open(filePath, FileMode.Create))
				using (StreamWriter sw = new StreamWriter(fs))
					sw.Write(val);
			}
			catch (JsonException ex)
			{
				MessageBox.Show("Cannot create json: " + ex.Message, "JSON error");
				return;
			}
			catch (SystemException ex)
			{
				MessageBox.Show("Cannot save file: " + ex.Message, "Error");
				return;
			}
		}

		private void LoadJson(string filePath)
		{
			try
			{
				string val = string.Empty;
				using (FileStream fs = File.Open(filePath, FileMode.Open))
				using (StreamReader sr = new StreamReader(fs))
					val = sr.ReadToEnd();
				_options = Options.Deserialize(val);
			}
			catch (JsonException ex)
			{
				MessageBox.Show("Cannot parse json: " + ex.Message, "JSON error");
				return;
			}
			catch (SystemException ex)
			{
				MessageBox.Show("Cannot load file: " + ex.Message, "Error");
				return;
			}
		}

		private void ResetJson()
		{
			_options = Options.Deserialize(string.Empty);
		}

		private void Build()
		{
			AttachConsole(ATTACH_PARENT_PROCESS);
			
			Console.WriteLine("Building NAppUpdater feed '{0}'", txtBaseURL.Text.Trim());
			if (string.IsNullOrEmpty(txtFeedXML.Text))
			{
				const string msg = "The feed file location needs to be defined.\n" + "The outputs cannot be generated without this.";
				if (_argParser.ShowGui) MessageBox.Show(msg);
				Console.WriteLine(msg);
				return;
			}
			// If the target folder doesn't exist, create a path to it
			string dest = txtFeedXML.Text.Trim();
			var destDir = Directory.GetParent(GetFullDirectoryPath(Path.GetDirectoryName(dest)));
			if (!Directory.Exists(destDir.FullName)) Directory.CreateDirectory(destDir.FullName);

			XmlDocument doc = new XmlDocument();
			XmlDeclaration dec = doc.CreateXmlDeclaration("1.0", "utf-8", null);

			doc.AppendChild(dec);
			XmlElement feed = doc.CreateElement("Feed");
			if (!string.IsNullOrEmpty(txtBaseURL.Text.Trim())) feed.SetAttribute("BaseUrl", txtBaseURL.Text.Trim());
			doc.AppendChild(feed);

			XmlElement tasks = doc.CreateElement("Tasks");

			Dictionary<string, StartStopOption> startStopProcesses = null;
			List<StartStopOption> atEnd = null;
			if (_options.LaunchFiles != null)
			{
				startStopProcesses = new Dictionary<string, StartStopOption>();
				
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

					startStopProcesses.Add(s.Target, s);
				}
			}

			Console.WriteLine("Processing feed items");
			int itemsCopied = 0;
			int itemsCleaned = 0;
			int itemsSkipped = 0;
			int itemsFailed = 0;
			int itemsMissingConditions = 0;
			foreach (ListViewItem thisItem in lstFiles.Items)
			{
				string destFile = "";
				string filename = "";
				try
				{
					filename = thisItem.Text;
					destFile = Path.Combine(destDir.FullName, filename);
				}
				catch { }
				if (destFile == "" || filename == "")
				{
					string msg = string.Format("The file could not be pathed:\nFolder:'{0}'\nFile:{1}", destDir.FullName, filename);
					if (_argParser.ShowGui) MessageBox.Show(msg);
					Console.WriteLine(msg);
					continue;
				}

				if (thisItem.Checked)
				{
					var fileInfoEx = (FileInfoEx)thisItem.Tag;
					StartStopOption startStopOpt = null;
					if (startStopProcesses != null && startStopProcesses.ContainsKey(fileInfoEx.RelativeName))
						startStopOpt = startStopProcesses[fileInfoEx.RelativeName];

					XmlElement task = doc.CreateElement("FileUpdateTask");
					task.SetAttribute("localPath", fileInfoEx.RelativeName);
                    // generate FileUpdateTask metadata items
                    task.SetAttribute("lastModified", fileInfoEx.FileInfo.LastWriteTime.ToFileTime().ToString(CultureInfo.InvariantCulture));
					if (!string.IsNullOrEmpty(txtAddExtension.Text))
					{
						task.SetAttribute("updateTo", AddExtensionToPath(fileInfoEx.RelativeName, txtAddExtension.Text));
					}

					task.SetAttribute("fileSize", fileInfoEx.FileInfo.Length.ToString(CultureInfo.InvariantCulture));
					if (!string.IsNullOrEmpty(fileInfoEx.FileVersion)) task.SetAttribute("version", fileInfoEx.FileVersion);

					XmlElement conds = doc.CreateElement("Conditions");
					XmlElement cond;

					//File Exists
					cond = doc.CreateElement("FileExistsCondition");
					cond.SetAttribute("type", "or-not");
					conds.AppendChild(cond);

					//Version
					if (chkVersion.Checked && !string.IsNullOrEmpty(fileInfoEx.FileVersion))
					{
						cond = doc.CreateElement("FileVersionCondition");
						cond.SetAttribute("type", "or");
						cond.SetAttribute("what", "below");
						cond.SetAttribute("version", fileInfoEx.FileVersion);
						conds.AppendChild(cond);
					}

					//Size
					if (chkSize.Checked)
					{
						cond = doc.CreateElement("FileSizeCondition");
						cond.SetAttribute("type", "or-not");
						cond.SetAttribute("what", "is");
						cond.SetAttribute("size", fileInfoEx.FileInfo.Length.ToString(CultureInfo.InvariantCulture));
						conds.AppendChild(cond);
					}

					//Date
					if (chkDate.Checked)
					{
						cond = doc.CreateElement("FileDateCondition");
						cond.SetAttribute("type", "or");
						cond.SetAttribute("what", "older");
						// local timestamp, not UTC
						cond.SetAttribute("timestamp", fileInfoEx.FileInfo.LastWriteTime.ToFileTime().ToString(CultureInfo.InvariantCulture));
						conds.AppendChild(cond);
					}

					//Hash
					if (chkHash.Checked)
					{
						cond = doc.CreateElement("FileChecksumCondition");
						cond.SetAttribute("type", "or-not");
						cond.SetAttribute("checksumType", "sha256");
						cond.SetAttribute("checksum", fileInfoEx.Hash);
						conds.AppendChild(cond);
					}

					if (conds.ChildNodes.Count == 0) itemsMissingConditions++;
					task.AppendChild(conds);

					if (startStopOpt != null && startStopOpt.When == "before")
						tasks.AppendChild(CreateXmlStartStopTask(startStopOpt, doc));

					tasks.AppendChild(task);

					if (startStopOpt != null && startStopOpt.When == "after")
						tasks.AppendChild(CreateXmlStartStopTask(startStopOpt, doc));

					if (chkCopyFiles.Checked)
					{
						if (CopyFile(fileInfoEx.FileInfo.FullName, destFile)) itemsCopied++;
						else itemsFailed++;
					}
				}
				else
				{
					try
					{
						if (chkCleanUp.Checked & File.Exists(destFile))
						{
							File.Delete(destFile);
							itemsCleaned += 1;
						}
						else itemsSkipped += 1;
					}
					catch (IOException)
					{
						itemsFailed += 1;
					}
				}
			}

			if (atEnd != null)
				foreach (StartStopOption s in atEnd)
					tasks.AppendChild(CreateXmlStartStopTask(s, doc));

			feed.AppendChild(tasks);

			string xmlDest = Path.Combine(destDir.FullName, Path.GetFileName(dest));
			doc.Save(xmlDest);

			// open the outputs folder if we're running from the GUI or 
			// we have an explicit command line option to do so
			if (!_argParser.HasArgs || _argParser.OpenOutputsFolder) OpenOutputsFolder();
			Console.WriteLine("Done building feed.");
			if (itemsCopied > 0) Console.WriteLine("{0,5} items copied", itemsCopied);
			if (itemsCleaned > 0) Console.WriteLine("{0,5} items cleaned", itemsCleaned);
			if (itemsSkipped > 0) Console.WriteLine("{0,5} items skipped", itemsSkipped);
			if (itemsFailed > 0) Console.WriteLine("{0,5} items failed", itemsFailed);
			if (itemsMissingConditions > 0) Console.WriteLine("{0,5} items without any conditions", itemsMissingConditions);
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

		private bool CopyFile(string sourceFile, string destFile)
		{
			// If the target folder doesn't exist, create the path to it
			var fi = new FileInfo(destFile);
			var d = Directory.GetParent(fi.FullName);
			if (!Directory.Exists(d.FullName)) CreateDirectoryPath(d.FullName);
			if (!string.IsNullOrEmpty(txtAddExtension.Text))
			{
				destFile = AddExtensionToPath(destFile, txtAddExtension.Text);
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

		private void OpenOutputsFolder()
		{
			string path = txtOutputFolder.Text.Trim();

			if (string.IsNullOrEmpty(path))
			{
				return;
			}

			string dir = GetFullDirectoryPath(path);

			CreateDirectoryPath(dir);
			Process process = new Process
			{
				StartInfo =
				{
					UseShellExecute = true,
					FileName = dir
				}
			};
			process.Start();
		}

		private int GetImageIndex(string ext)
		{
			switch (ext.Trim('.'))
			{
				case "bmp":
					return 1;
				case "dll":
					return 2;
				case "doc":
				case "docx":
					return 3;
				case "exe":
					return 4;
				case "htm":
				case "html":
					return 5;
				case "jpg":
				case "jpeg":
					return 6;
				case "pdf":
					return 7;
				case "png":
					return 8;
				case "txt":
					return 9;
				case "wav":
				case "mp3":
					return 10;
				case "wmv":
					return 11;
				case "xls":
				case "xlsx":
					return 12;
				case "zip":
					return 13;
				default:
					return 0;
			}
		}

		private void ReadFiles()
		{
			string outputDir = GetFullDirectoryPath(txtOutputFolder.Text.Trim());

			if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
			{
				return;
			}

			outputDir = GetFullDirectoryPath(outputDir);

			lstFiles.BeginUpdate();
			lstFiles.Items.Clear();

			FileSystemEnumerator enumerator = new FileSystemEnumerator(outputDir, "*.*", true);
			foreach (FileInfo fi in enumerator.Matches())
			{
				string filePath = fi.FullName;

				if ((IsIgnorable(filePath)))
				{
					continue;
				}

				FileInfoEx fileInfo = new FileInfoEx(filePath, outputDir.Length);

				ListViewItem item = new ListViewItem(fileInfo.RelativeName, GetImageIndex(fileInfo.FileInfo.Extension));
				item.SubItems.Add(fileInfo.FileVersion);
				item.SubItems.Add(fileInfo.FileInfo.Length.ToString(CultureInfo.InvariantCulture));
				item.SubItems.Add(fileInfo.FileInfo.LastWriteTime.ToString(CultureInfo.InvariantCulture));
				item.SubItems.Add(fileInfo.Hash);
				item.Checked = (!_options.IgnoreFiles.Contains(fileInfo.RelativeName));
				item.Tag = fileInfo;
				lstFiles.Items.Add(item);
			}

			lstFiles.EndUpdate();
		}

		private string GetFullDirectoryPath(string path)
		{
			string absolutePath = path;

			if (!Path.IsPathRooted(absolutePath))
			{
				absolutePath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), path);
			}

			if (!absolutePath.EndsWith("\\"))
			{
				absolutePath += "\\";
			}

			return Path.GetFullPath(absolutePath);
		}

		private bool IsIgnorable(string filename)
		{
			string ext = Path.GetExtension(filename);
			if ((chkIgnoreSymbols.Checked && ext == ".pdb")) return true;
			return (chkIgnoreVsHost.Checked && filename.ToLower().Contains("vshost.exe"));
		}

		private string AddExtensionToPath(string filePath, string extension)
		{
			string sanitizedExtension = (extension.Trim().StartsWith(".") ? String.Empty : ".") + extension.Trim();
			return filePath + sanitizedExtension;
		}

		private void Save(bool forceDialog)
		{
			SaveFormSettings();
			if (forceDialog || string.IsNullOrEmpty(FileName))
			{
				SaveFileDialog dlg = new SaveFileDialog
				{
					Filter = DialogFilter,
					FileName = DefaultFileName
				};
				DialogResult result = dlg.ShowDialog();
				if (result == DialogResult.OK)
				{
					SaveJson(dlg.FileName);
					FileName = dlg.FileName;
				}
			}
			else
			{
				SaveJson(FileName);
			}
			UpdateTitle();
		}

		#endregion

		private void frmMain_DragEnter(object sender, DragEventArgs e)
		{
			string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
			if (files.Length == 0) return;
			e.Effect = files[0].EndsWith(".config") ? DragDropEffects.Move : DragDropEffects.None;
		}

		private void frmMain_DragDrop(object sender, DragEventArgs e)
		{
			string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
			if (files.Length == 0) return;
			try
			{
				string fileName = files[0];
				LoadJson(fileName);
				FileName = fileName;
				InitializeFormSettings();
			}
			catch (Exception ex)
			{
				MessageBox.Show("The file could not be opened: \n" + ex.Message);
			}
		}

		private static readonly int ATTACH_PARENT_PROCESS = -1;

		[DllImport("kernel32.dll")]
		private static extern bool AttachConsole(int dwProcessId);

		[DllImport("kernel32.dll")]
		private static extern bool FreeConsole();
	}
}
