using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NAppUpdate.Framework.Common;
using NAppUpdate.Framework.Sources;

namespace NAppUpdate.Framework.Tasks
{
	[Serializable]
	[UpdateTaskAlias("startProcess")]
	class StartProcessTask : UpdateTaskBase
	{
		[NauField("name", "Filename to execute", true)]
		public string Filename { get; set; }

		[NauField("args", "Arguments", false)]
		public string Arguments { get; set; }

		[NauField("shell", "Use shell to execute", false)]
		public bool UseShellExecute { get; set; }

		private string _appDir;

		public override void Prepare(IUpdateSource source)
		{
			_appDir = Path.GetDirectoryName(UpdateManager.Instance.ApplicationPath);
		}

		public override TaskExecutionStatus Execute(bool coldRun)
		{
			if (!coldRun)
				return TaskExecutionStatus.RequiresAppRestart;
			
			Thread.Sleep(1000);
			string filePath = Path.Combine(_appDir, Filename);
			if (!File.Exists(filePath))
			{
				UpdateManager.Instance.Logger.Log(Logger.SeverityLevel.Error, "File not exist: ");
				throw new UpdateProcessFailedException("File not exist: " + filePath);
			}

			ProcessStartInfo info = new ProcessStartInfo
			{
				UseShellExecute = UseShellExecute,
				FileName = Filename,
				WorkingDirectory = _appDir,
				Arguments = Arguments
			};

			Process.Start(info);
			return TaskExecutionStatus.Successful;
		}

		public override bool Rollback()
		{
			return true;
		}
	}
}
