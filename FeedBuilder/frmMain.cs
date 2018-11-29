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
using System.Reflection;
using Newtonsoft.Json;

namespace FeedBuilder
{
	class JsonOptions
	{
		public string BaseURL { get; set; }
		public bool CleanUp { get; set; }
		public bool CompareDate { get; set; }
		public bool CompareHash { get; set; }
		public bool CompareSize { get; set; }
		public bool CompareVersion { get; set; }
		public bool CopyFiles { get; set; }
		public string FeedXML { get; set; }
		public bool IgnoreDebugSymbols { get; set; }
		public IList<string> IgnoreFiles { get; set; }
		public bool IgnoreVsHosting { get; set; }
		public string OutputFolder { get; set; }
		public string AddExtension { get; set; }

		public static JsonOptions Deserialize(string json)
		{
			JsonOptions opts = JsonConvert.DeserializeObject<JsonOptions>(json);
			return opts;
		}

		public static string Serialize(JsonOptions opts)
		{
			string json = JsonConvert.SerializeObject(opts, new JsonSerializerSettings
			{
				NullValueHandling = NullValueHandling.Ignore
			});
			return json;
		}
	}

	public partial class frmMain : Form
	{
		public frmMain()
		{
			InitializeComponent();
		}

		#region " Private constants/variables"

		private const string DialogFilter = "Feed configuration files (*.config)|*.config|All files (*.*)|*.*";
		private const string DefaultFileName = "FeedBuilder.config";
		private OpenFileDialog _openDialog;
		private JsonOptions _options;

		#endregion

		private ArgumentsParser _argParser;

		#region " Properties"

		public string FileName { get; set; }
		public bool ShowGui { get; set; }

		#endregion

		#region " Loading/Initialization/Lifetime"

		private void frmMain_Load(Object sender, EventArgs e)
		{
			Visible = false;
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
			if (!string.IsNullOrEmpty(FileName))
			{
				if (File.Exists(FileName))
				{
					FeedBuilderSettingsProvider p = new FeedBuilderSettingsProvider();
					p.LoadFrom(FileName);
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
			LoadJsonOptions();
			if (string.IsNullOrEmpty(_options.OutputFolder))
			{
				txtOutputFolder.Text = string.Empty;
			}
			else
			{
				string path = GetFullDirectoryPath(_options.OutputFolder);
				txtOutputFolder.Text = Directory.Exists(path) ? _options.OutputFolder : string.Empty;
			}

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

			if (_options.IgnoreFiles == null)
				_options.IgnoreFiles = new List<string>();

			ReadFiles();
			UpdateTitle();
		}

		private void UpdateTitle()
		{
			if (string.IsNullOrEmpty(FileName)) Text = "Feed Builder";
			else Text = "Feed Builder - " + FileName;
		}

		private void SaveFormSettings()
		{
			if (!string.IsNullOrEmpty(txtOutputFolder.Text.Trim()) && Directory.Exists(txtOutputFolder.Text.Trim()))
				_options.OutputFolder = txtOutputFolder.Text.Trim();
			// ReSharper disable AssignNullToNotNullAttribute
			if (!string.IsNullOrEmpty(txtFeedXML.Text.Trim()) && Directory.Exists(Path.GetDirectoryName(txtFeedXML.Text.Trim())))
				_options.FeedXML = txtFeedXML.Text.Trim();
			// ReSharper restore AssignNullToNotNullAttribute
			if (!string.IsNullOrEmpty(txtBaseURL.Text.Trim()))
				_options.BaseURL = txtBaseURL.Text.Trim();

            if (!string.IsNullOrEmpty(txtAddExtension.Text.Trim()))
				_options.AddExtension = txtAddExtension.Text.Trim();

            _options.CompareVersion = chkVersion.Checked;
			_options.CompareSize = chkSize.Checked;
			_options.CompareDate = chkDate.Checked;
			_options.CompareHash = chkHash.Checked;

			_options.IgnoreDebugSymbols = chkIgnoreSymbols.Checked;
			_options.IgnoreVsHosting = chkIgnoreVsHost.Checked;
			_options.CopyFiles = chkCopyFiles.Checked;
			_options.CleanUp = chkCleanUp.Checked;

			if (_options.IgnoreFiles == null)
				_options.IgnoreFiles = new List<string>();
			_options.IgnoreFiles.Clear();

			foreach (ListViewItem thisItem in lstFiles.Items)
			{
				if (!thisItem.Checked)
					_options.IgnoreFiles.Add(thisItem.Text);
			}
		}

		private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
		{
			SaveFormSettings();
			SaveJsonOptions();
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
			_options = new JsonOptions();
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
			FeedBuilderSettingsProvider p = new FeedBuilderSettingsProvider();
			p.LoadFrom(dlg.FileName);
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
			ReadFiles();
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

		private void LoadJsonOptions()
		{
			
		}

		private void SaveJsonOptions()
		{

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
					tasks.AppendChild(task);

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
				item.Checked = (!_ignoredFiles.Contains(fileInfo.RelativeName));
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
					FeedBuilderSettingsProvider p = new FeedBuilderSettingsProvider();
					p.SaveAs(dlg.FileName);
					FileName = dlg.FileName;
				}
			}
			else
			{
				FeedBuilderSettingsProvider p = new FeedBuilderSettingsProvider();
				p.SaveAs(FileName);
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
				FeedBuilderSettingsProvider p = new FeedBuilderSettingsProvider();
				p.LoadFrom(fileName);
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
