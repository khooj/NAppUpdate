using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NAppUpdate.Framework.Common;
using NAppUpdate.Framework.Sources;

namespace NAppUpdate.Framework.Tasks
{
    [Serializable]
    [UpdateTaskAlias("stopProcess")]
    class StopProcessTask : UpdateTaskBase
    {
        [NauField("name", "Process name to stop", true)]
        public string ProcessName { get; set; }

        public override void Prepare(IUpdateSource source)
        {
        }

        public override TaskExecutionStatus Execute(bool coldRun)
        {
            if (!coldRun)
                return TaskExecutionStatus.RequiresAppRestart;

			if (ProcessName.EndsWith(".exe"))
			{
				ProcessName = ProcessName.Remove(ProcessName.Length - 4);
			}

            Process[] procs = Process.GetProcessesByName(ProcessName);
			foreach (Process proc in procs)
            {
                proc.Kill();
                proc.WaitForExit();
                proc.Dispose();
            }

            return TaskExecutionStatus.Successful;
        }

        public override bool Rollback()
        {
            return true;
        }
    }
}
