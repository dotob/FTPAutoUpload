using System;
using System.IO;
using System.Net;
using System.Threading;

namespace FTPAutoUpload {
	class Program {
		private static int counter = 0;

		static void Main(string[] args) {
			var watchdir = Properties.Settings.Default.watchdir;
			if (Directory.Exists(watchdir)) {
				var watcher = new FileSystemWatcher();
				watcher.Path = watchdir;
				watcher.Filter = Properties.Settings.Default.watchpattern;
				watcher.Created += watcher_Changed;
				watcher.EnableRaisingEvents = true;
			}
			else {
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Watch dir {0} does not exist", watchdir);
			}
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("Press any key to stop!");
			Console.ReadKey();
		}

		private static void watcher_Changed(object sender, FileSystemEventArgs e) {
			counter++;
			var localFileNameWithPath = e.FullPath;
			if (counter%Properties.Settings.Default.everynthfile == 0) {
				WaitForFileAndUpload(localFileNameWithPath);
			}
			else {
				Console.ForegroundColor = ConsoleColor.White;
				Console.WriteLine("Ignore file {0} ({1}/{2})", localFileNameWithPath, counter % Properties.Settings.Default.everynthfile, Properties.Settings.Default.everynthfile);
			}
		}

		private static void WaitForFileAndUpload(string localFileNameWithPath) {
			WaitForFile(new FileInfo(localFileNameWithPath));

			Console.ForegroundColor = ConsoleColor.DarkGreen;
			Console.WriteLine("Start upload file {0}", localFileNameWithPath);
			string rd = Properties.Settings.Default.remotedir;
			string rf = Path.GetFileName(localFileNameWithPath);
			string host = Properties.Settings.Default.ftphost;
			string user = Properties.Settings.Default.ftpuser;
			string pass = Properties.Settings.Default.ftppass;
			string cf = Properties.Settings.Default.constantFileName;
			// upload once with original file name
			var success = UploadFtpFile(localFileNameWithPath, rd, rf, host, user, pass);
			if (success) {
				Console.WriteLine("Finished upload file {0} to {1}{2} (original file name)", localFileNameWithPath, rd, rf);
			}
			// upload second time 
			if (!string.IsNullOrWhiteSpace(cf)) {
				success = UploadFtpFile(localFileNameWithPath, rd, cf, host, user, pass);
				if (success) {
					Console.WriteLine("Finished upload file {0} to {1}{2} (constant file name)", localFileNameWithPath, rd, cf);
				}
			}
			else {
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("No constant file name given. Skip second upload.");
			}
		}

		private static void WaitForFile(FileInfo file) {
			FileStream stream = null;
			bool fileReady = false;
			while (!fileReady) {
				try {
					using (stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
						fileReady = true;
					}
				} catch (IOException) {
					//File isn't ready yet, so we need to keep on waiting until it is.
					Console.ForegroundColor = ConsoleColor.Cyan;
					Console.WriteLine("File {0} not yet ready...waiting", file.Name);
				}
				//We'll want to wait a bit between polls, if the file isn't ready.
				if (!fileReady) Thread.Sleep(500);
			}
		}

		public static bool UploadFtpFile(string localFileNameWithPath, string remoteFolderName, string remoteFileNameNoPath, string host, string user, string pass) {
			var success = false;
			try {
				var request = WebRequest.Create(new Uri(string.Format(@"ftp://{0}/{1}/{2}", host, remoteFolderName, remoteFileNameNoPath))) as FtpWebRequest;
				if (request != null) {
					request.Method = WebRequestMethods.Ftp.UploadFile;
					request.UseBinary = true;
					request.UsePassive = true;
					request.KeepAlive = true;
					request.Credentials = new NetworkCredential(user, pass);
					request.ConnectionGroupName = "group";

					using (var fs = File.OpenRead(localFileNameWithPath)) {
						var buffer = new byte[fs.Length];
						fs.Read(buffer, 0, buffer.Length);
						fs.Close();
						var requestStream = request.GetRequestStream();
						requestStream.Write(buffer, 0, buffer.Length);
						requestStream.Flush();
						requestStream.Close();
						success = true;
					}
				}
				else {
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine("Could not create Webrequest");
				}
			} catch (Exception ex) {
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Error: {0}", ex);
			}
			return success;
		}
	}
}
