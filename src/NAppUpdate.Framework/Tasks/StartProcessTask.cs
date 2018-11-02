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

		public override void Prepare(IUpdateSource source)
		{
		}

		public override TaskExecutionStatus Execute(bool coldRun)
		{
			Thread.Sleep(1000);
			ProcessStartInfo info = new ProcessStartInfo
			{
				UseShellExecute = UseShellExecute,
				FileName = Filename,
				WorkingDirectory = Path.GetDirectoryName(UpdateManager.Instance.ApplicationPath),
				Arguments = Arguments
			};

			if (!File.Exists(Path.Combine(Path.GetDirectoryName(UpdateManager.Instance.ApplicationPath), Filename)))
			{
				Console.WriteLine("File not exists");
			}

			Process p = Process.Start(info);
			return p.HasExited ? TaskExecutionStatus.Failed : TaskExecutionStatus.Successful;
		}

		public override bool Rollback()
		{
			return true;
		}
	}
}
