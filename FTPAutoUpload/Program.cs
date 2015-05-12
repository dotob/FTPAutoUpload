using System;
using System.IO;
using System.Net;
using System.Threading;

namespace FTPAutoUpload {
	class Program {
		private static int counter = 0;
		private static readonly bool test = Properties.Settings.Default.simulate;
		private static readonly NetworkCredential uspw = new NetworkCredential(Properties.Settings.Default.ftpuser, Properties.Settings.Default.ftppass);

		static void Main() {
			var watchdir = Properties.Settings.Default.watchdir;
			if (Directory.Exists(watchdir)) {
				var watcher = new FileSystemWatcher();
				watcher.Path = watchdir;
				watcher.IncludeSubdirectories = true;
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
			WaitForFile(new FileInfo(localFileNameWithPath));
			if (counter % Properties.Settings.Default.everynthfile == 0) {
				UploadNewFile(localFileNameWithPath);
			}
			else {
				Console.ForegroundColor = ConsoleColor.White;
				Console.WriteLine("Ignore file {0} ({1}/{2})", localFileNameWithPath, counter % Properties.Settings.Default.everynthfile, Properties.Settings.Default.everynthfile);
			}			
			
			UploadAndRotate(localFileNameWithPath);
		}

		private static void UploadNewFile(string localFileNameWithPath) {
			Console.ForegroundColor = ConsoleColor.DarkGreen;
			Console.WriteLine("Start upload new file {0}", localFileNameWithPath);
			string rd = Properties.Settings.Default.remotedir;
			string rf = Path.GetFileName(localFileNameWithPath);
			string host = Properties.Settings.Default.ftphost;

			var wdfp = Path.GetFullPath(Properties.Settings.Default.watchdir).Replace('\\', '/');
			var lffp = Path.GetFullPath(localFileNameWithPath).Replace('\\', '/');

			var remoteDirToCheck = rd;
			if (lffp.StartsWith(wdfp)) {
				var addToRemoteDir = lffp.Substring(wdfp.Length);
				rd = string.Format("{0}{1}", rd, addToRemoteDir);
				remoteDirToCheck = Path.GetDirectoryName(rd).Replace('\\', '/');
			}
			// make dir
			CreateFtpDir(remoteDirToCheck, host);

			// upload once with original file name
			var success = UploadFtpFile(localFileNameWithPath, remoteDirToCheck, rf, host);
			if (success) {
				Console.ForegroundColor = ConsoleColor.DarkGreen;
				Console.WriteLine("  Finished upload file {0} to {1}{2} (original file name)", localFileNameWithPath, rd, rf);
			}
		}
		
		private static void UploadAndRotate(string localFileNameWithPath) {
			Console.ForegroundColor = ConsoleColor.DarkGreen;
			Console.WriteLine("Start upload rotate file {0}", localFileNameWithPath);
			string host = Properties.Settings.Default.ftphost;
			string cf = Properties.Settings.Default.constantpattern;
			string cd = Properties.Settings.Default.constantdir;
			int cc = Properties.Settings.Default.constantcount;

			// upload last cc count of files rotate filenames
			if (!string.IsNullOrWhiteSpace(cf)) {
				var cfp = string.Format("{0}/{1}", cd, cf);
				// at first remove last file of rotation
				var lastRotationFileName = string.Format(cfp, cc);
				var success = DeleteFtpFile(lastRotationFileName, host);
				if (success) {
					Console.ForegroundColor = ConsoleColor.DarkGreen;
					Console.WriteLine("  Deleted {0}", lastRotationFileName);
				}

				// then rename all other rotation files
				for (int i = cc; i > 1; i--) {
					var oldPath = string.Format(cfp, i-1);
					var newPath = string.Format("/"+cfp, i);
					success = RenameFtpFile(oldPath, newPath, host);
					if (success) {
						Console.ForegroundColor = ConsoleColor.DarkGreen;
						Console.WriteLine("  Renamed {0} to {1}", oldPath, newPath);
					}
				}

				// upload new file
				var newFilename = string.Format(cf, 1);
				success = UploadFtpFile(localFileNameWithPath, cd, newFilename, host);
				if (success) {
					Console.ForegroundColor = ConsoleColor.DarkGreen;
					Console.WriteLine("  Finished upload file {0} to {1}/{2} (constant file name)", localFileNameWithPath, cd, newFilename);
				}
			}
			else {
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("No constant file name given. Skip second upload.");
			}
		}

		private static void WaitForFile(FileInfo file) {
			bool fileReady = false;
			while (!fileReady) {
				try {
					using (file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
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

		public static bool UploadFtpFile(string localFileNameWithPath, string remoteFolderName, string remoteFileNameNoPath, string host) {
			Console.ForegroundColor = ConsoleColor.Blue;
			Console.WriteLine("  UploadFtpFile({0}, {1}, {2}, {3})", localFileNameWithPath, remoteFolderName, remoteFileNameNoPath, host);
			if (test) {
				return true;
			}
			var success = false;
			try {
				var request = WebRequest.Create(new Uri(string.Format(@"ftp://{0}/{1}/{2}", host, remoteFolderName, remoteFileNameNoPath))) as FtpWebRequest;
				if (request != null) {
					request.Method = WebRequestMethods.Ftp.UploadFile;
					request.UseBinary = true;
					request.UsePassive = true;
					request.KeepAlive = true;
					request.Credentials = uspw;

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
					Console.WriteLine("Could not create upload Webrequest");
				}
			} catch (Exception ex) {
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Error: {0}", ex);
			}
			return success;
		}

		public static bool RenameFtpFile(string oldPath, string newPath, string host) {
			Console.ForegroundColor = ConsoleColor.Blue;
			Console.WriteLine("  RenameFtpFile({0}, {1}, {2})", oldPath, newPath, host);
			if (test) {
				return true;
			}
			var success = false;
			try {
				var requestUri = new Uri(string.Format(@"ftp://{0}/{1}", host, oldPath));
				var request = WebRequest.Create(requestUri) as FtpWebRequest;
				if (request != null) {
					request.Method = WebRequestMethods.Ftp.Rename;
					request.Credentials = uspw;
					request.RenameTo = newPath;
					request.GetResponse();
					success = true;
				}
				else {
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine("Could not create rename Webrequest");
				}
			} catch (Exception ex) {
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Error: {0}", ex);
			}
			return success;
		}

		public static bool DeleteFtpFile(string path, string host) {
			Console.ForegroundColor = ConsoleColor.Blue;
			Console.WriteLine("  DeleteFtpFile({0}, {1})", path, host);
			if (test) {
				return true;
			} 
			var success = false;
			try {
				var request = WebRequest.Create(new Uri(string.Format(@"ftp://{0}/{1}", host, path))) as FtpWebRequest;
				if (request != null) {
					request.Method = WebRequestMethods.Ftp.DeleteFile;
					request.Credentials = uspw;
					request.GetResponse();
					success = true;
				}
				else {
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine("Could not create delete Webrequest");
				}
			} catch (Exception ex) {
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Error: {0}", ex);
			}
			return success;
		}

		public static bool CreateFtpDir(string path, string host) {
			Console.ForegroundColor = ConsoleColor.Blue;
			Console.WriteLine("  CreateFtpDir({0}, {1})", path, host);
			if (test) {
				return true;
			} 
			var success = false;
			try {
				var request = WebRequest.Create(new Uri(string.Format(@"ftp://{0}/{1}", host, path))) as FtpWebRequest;
				if (request != null) {
					request.Method = WebRequestMethods.Ftp.MakeDirectory;
					request.Credentials = uspw;
					request.GetResponse();
					success = true;
				}
				else {
					// do not tell anyone
					//Console.ForegroundColor = ConsoleColor.Red;
					//Console.WriteLine("Could not create mkdir Webrequest");
				}
			} catch (Exception ex) {
				//Console.ForegroundColor = ConsoleColor.Red;
				//Console.WriteLine("Error: {0}", ex);
			}
			return success;
		}
	}
}
