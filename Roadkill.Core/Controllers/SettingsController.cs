﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Text;
using System.Web.Security;
using System.IO;
using System.Web.Configuration;
using System.Configuration;
using Ionic.Zip;
using Ionic.Zlib;
using System.Diagnostics;
using Roadkill.Core.Search;

namespace Roadkill.Core.Controllers
{
	/// <summary>
	/// Provides functionality for the settings page including tools and user management.
	/// </summary>
	/// <remarks>All actions in this controller require admin rights.</remarks>
	[AdminRequired]
	public class SettingsController : ControllerBase
    {
		/// <summary>
		/// The default settings page that displays the current Roadkill settings.
		/// </summary>
		/// <returns>A <see cref="SettingsSummary"/> as the model.</returns>
		public ActionResult Index()
		{
			SettingsSummary summary = SettingsSummary.GetCurrentSettings();
			return View(summary);
		}

		/// <summary>
		/// Saves the <see cref="SettingsSummary"/> that is POST'd to the action.
		/// </summary>
		/// <param name="summary">The settings to save to the web.config/database.</param>
		/// <returns>A <see cref="SettingsSummary"/> as the model.</returns>
		[HttpPost]
		public ActionResult Index(SettingsSummary summary)
		{
			if (ModelState.IsValid)
			{
				SettingsManager.SaveWebConfigSettings(summary);
				SettingsManager.SaveSiteConfiguration(summary, false);
			}
			return View(summary);
		}

		/// <summary>
		/// Displays the Users view.
		/// </summary>
		/// <returns>An <see cref="Ilist`IEnumerable`string"/> as the model. The first item contains a list of admin users,
		/// the second item contains a list of editor users. If Windows authentication is being used, the action uses the 
		/// UsersForWindows view.</returns>
		[ImportModelState]
		public ActionResult Users()
		{
			IList<IEnumerable<string>> list = new List<IEnumerable<string>>();
			list.Add(SecurityManager.Current.ListAdmins());
			list.Add(SecurityManager.Current.ListEditors());

			if (SecurityManager.Current.IsReadonly)
				return View("UsersReadOnly", list);
			else
				return View(list);
		}

		/// <summary>
		/// Adds an admin user to the system, validating the <see cref="UserSummary"/> first.
		/// </summary>
		/// <param name="summary">The user details to add.</param>
		/// <returns>Redirects to the Users action. Additionally, if an error occured, TempData["action"] contains the string "addadmin".</returns>
		[HttpPost]
		[ExportModelState]
		public ActionResult AddAdmin(UserSummary summary)
		{
			if (ModelState.IsValid)
			{
				SecurityManager.Current.AddUser(summary.NewUsername, summary.Password, true, false);

				// TODO
				// ModelState.AddModelError("General", errors);
			}
			else
			{
				// Instructs the view to reshow the modal dialog
				TempData["action"] = "addadmin";
			}

			return RedirectToAction("Users");
		}

		/// <summary>
		/// Adds an editor user to the system, validating the <see cref="UserSummary"/> first.
		/// </summary>
		/// <param name="summary">The user details to add.</param>
		/// <returns>Redirects to the Users action. Additionally, if an error occured, TempData["action"] contains the string "addeditor".</returns>
		[HttpPost]
		[ExportModelState]
		public ActionResult AddEditor(UserSummary summary)
		{
			if (ModelState.IsValid)
			{
				try
				{
					SecurityManager.Current.AddUser(summary.NewUsername, summary.Password, false, true);
				}
				catch (SecurityException e)
				{
					ModelState.AddModelError("General", e.Message);
				}
			}
			else
			{
				// Instructs the view to reshow the modal dialog
				TempData["action"] = "addeditor";
			}

			return RedirectToAction("Users");
		}
		
		/// <summary>
		/// Edits an existing user. If the <see cref="UserSummary.Password"/> property is not blank, the password
		/// for the user is reset and then changed.
		/// </summary>
		/// <param name="summary">The user details to edit.</param>
		/// <returns>Redirects to the Users action. Additionally, if an error occured, TempData["edituser"] contains the string "addeditor".</returns>
		[HttpPost]
		[ExportModelState]
		public ActionResult EditUser(UserSummary summary)
		{
			if (ModelState.IsValid)
			{
				if (summary.UsernameHasChanged)
				{
					SecurityManager.Current.ChangeEmail(summary.ExistingUsername, summary.NewUsername);
					summary.ExistingUsername = summary.NewUsername;
				}

				if (!string.IsNullOrEmpty(summary.Password))
					SecurityManager.Current.ChangePassword(summary.ExistingUsername, summary.Password, summary.ExistingUsername);
			}
			else
			{
				// Instructs the view to reshow the modal dialog
				TempData["action"] = "edituser";
			}

			return RedirectToAction("Users");
		}

		/// <summary>
		/// Removes a user from the system.
		/// </summary>
		/// <param name="id">The id of the user to remove.</param>
		/// <returns>Redirects to the Users action.</returns>
		public ActionResult DeleteUser(string id)
		{
			SecurityManager.Current.DeleteUser(id);
			return RedirectToAction("Users");
		}

		/// <summary>
		/// Displays the tools page.
		/// </summary>
		public ActionResult Tools()
		{
			return View();
		}

		/// <summary>
		/// Exports the pages of site including their history as a single XML file.
		/// </summary>
		/// <returns>A <see cref="FileStreamResult"/> called 'roadkill-export.xml' containing the XML data.
		/// If an error occurs, a <see cref="HttpNotFound"/> result is returned and the error message written to the trace.</returns>
		public ActionResult ExportAsXml()
		{
			try
			{

				PageManager manager = new PageManager();
				string xml = manager.ExportToXml();

				// Let the FileStreamResult dispose the stream
				MemoryStream stream = new MemoryStream();
				StreamWriter writer = new StreamWriter(stream);
				writer.Write(xml);
				writer.Flush();
				stream.Position = 0;

				FileStreamResult result = new FileStreamResult(stream, "text/xml");
				result.FileDownloadName = "roadkill-export.xml";

				return result;
			}
			catch (IOException e)
			{
				Trace.Write(string.Format("Unable to export as XML: {0}", e));
				return HttpNotFound("There was a problem with exporting as XML. Enable tracing to see the error source");
			}
		}

		/// <summary>
		/// Exports the pages of the site as .wiki files, in ZIP format.
		/// </summary>
		/// <returns>A <see cref="FileStreamResult"/> called 'export-{date}.zip'. This file is saved in the App_Data folder first.
		/// If an error occurs, a <see cref="HttpNotFound"/> result is returned and the error message written to the trace.</returns>
		/// </returns>
		public ActionResult ExportAsWikiFiles()
		{
			PageManager manager = new PageManager();
			IEnumerable<PageSummary> pages = manager.AllPages();

			try
			{
				string exportFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + @"\App_Data", "export");
				Directory.CreateDirectory(exportFolder);

				string zipFilename = string.Format("export-{0}.zip", DateTime.Now.ToString("yyyy-MM-dd-HHmm"));
				string zipFullPath = Path.Combine(exportFolder, zipFilename);
				using (ZipFile zip = new ZipFile(zipFullPath))
				{

					foreach (PageSummary summary in pages)
					{
						string filePath = Path.Combine(exportFolder, summary.Title.AsValidFilename() + ".wiki");
						string content = "Tags:" + summary.Tags.SpaceDelimitTags() + "\r\n" + summary.Content;

						System.IO.File.WriteAllText(filePath, content);
						zip.AddFile(filePath, "");
					}

					zip.Save();
				}

				return File(zipFullPath, "application/zip", zipFullPath);
			}
			catch (IOException e)
			{
				Trace.Write(string.Format("Unable to export files: {0}", e));
				return HttpNotFound("There was a problem with the export. Enable tracing to see the error source");
			}
		}

		/// <summary>
		/// Exports the Attachments folder contents (including subdirectories) in ZIP format.
		/// </summary>
		/// <returns>A <see cref="FileStreamResult"/> called 'attachments-export-{date}.zip'. This file is saved in the App_Data folder first.
		/// If an error occurs, a <see cref="HttpNotFound"/> result is returned and the error message written to the trace.</returns>
		/// </returns>
		public ActionResult ExportAttachments()
		{
			PageManager manager = new PageManager();
			IEnumerable<PageSummary> pages = manager.AllPages();

			try
			{
				string exportFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory +@"\App_Data", "export");
				Directory.CreateDirectory(exportFolder);

				string zipFilename = string.Format("attachments-export-{0}.zip", DateTime.Now.ToString("yyy-MM-dd-HHss"));
				string zipFullPath = Path.Combine(exportFolder, zipFilename);
				using (ZipFile zip = new ZipFile(zipFullPath))
				{
					zip.AddDirectory(Server.MapPath(RoadkillSettings.AttachmentsFolder), "Attachments");
					zip.Save();
				}

				return File(zipFullPath, "application/zip", zipFullPath);
			}
			catch (IOException e)
			{
				Trace.Write(string.Format("Unable to export files: {0}", e));
				return HttpNotFound("There was a problem with the attachments export. Enable tracing to see the error source");
			}
		}

		/// <summary>
		/// Attempts to import page data and files from a Screwturn wiki database.
		/// </summary>
		/// <param name="screwturnConnectionString">The connection string to the Screwturn database.</param>
		/// <returns>Redirects to the Tools action.</returns>
		[HttpPost]
		public ActionResult ImportFromScrewTurn(string screwturnConnectionString)
		{
			ScrewTurnImporter importer = new ScrewTurnImporter();
			importer.ImportFromSql(screwturnConnectionString);
			TempData["Message"] = "Import successful";

			return RedirectToAction("Tools");
		}

		/// <summary>
		/// Deletes and re-creates the search index.
		/// </summary>
		/// <returns>Redirects to the Tools action.</returns>
		public ActionResult UpdateSearchIndex()
		{
			TempData["Message"] = "Update complete";
			SearchManager.Current.CreateIndex();
			return RedirectToAction("Tools");
		}

		/// <summary>
		/// Clears all wiki pages from the database.
		/// </summary>
		/// <returns>Redirects to the Tools action.</returns>
		public ActionResult ClearPages()
		{
			TempData["Message"] = "Database cleared";
			SettingsManager.ClearPageTables();
			return RedirectToAction("Tools");
		}
    }
}