using System;
using System.Net;
using NAppUpdate.Framework.Common;

namespace NAppUpdate.Framework.Sources
{
	public interface IUpdateSource
	{
		string GetUpdatesFeed(); // TODO: return a the feed as a stream
		bool GetData(string filePath, string basePath, Action<UpdateProgressInfo> onProgress, ref string tempLocation);
		void SetCredentials(ICredentials credentials);
	}
}
