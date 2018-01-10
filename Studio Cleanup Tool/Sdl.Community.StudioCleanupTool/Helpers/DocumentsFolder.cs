﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sdl.Community.StudioCleanupTool.Model;

namespace Sdl.Community.StudioCleanupTool.Helpers
{
    public static class DocumentsFolder
    {
	    public static async Task<List<string>> GetDocumentsFolderPath(string userName,
		    List<StudioVersionListItem> studioVersions,
		    List<StudioLocationListItem> locations)
	    {
			var documentsFolderLocationList = new List<string>();
		    foreach (var location in locations)
			{
				if (location.Alias.Equals("projectsXml"))
				{
					var projectsXmlFolderPath =
						await Task.FromResult(GetProjectsFolderPath(userName,
							studioVersions));
					documentsFolderLocationList.AddRange(projectsXmlFolderPath);
				}

				if (location.Alias.Equals("projectTemplates"))
				{
					var projectTemplatesFolderPath = await Task.FromResult(GetProjectTemplatesFolderPath(userName, studioVersions));
					documentsFolderLocationList.AddRange(projectTemplatesFolderPath);

				}
			}
		    return documentsFolderLocationList;
	    }

	    private static List<string> GetProjectTemplatesFolderPath(string userName, List<StudioVersionListItem> studioVersions)
	    {
		   var projectTempletesPath = new List<string>();
		    foreach (var studioVersion in studioVersions)
		    {
			    var ptojectTemplate = string.Format(@"C:\Users\{0}\Documents\{1}\Project Templates", userName,
				    studioVersion.DisplayName);
			    projectTempletesPath.Add(ptojectTemplate);
			}
		    return projectTempletesPath;
	    }

	    private static List<string> GetProjectsFolderPath(string userName,List<StudioVersionListItem> studioVersions)
	    {
			var xmlProjectsPaths = new List<string>();
		    foreach (var studioVersion in studioVersions)
		    {
			    var projectsXmlPath = string.Format(@"C:\Users\{0}\Documents\{1}\Projects\projects.xml", userName,
				    studioVersion.DisplayName);
				xmlProjectsPaths.Add(projectsXmlPath);
		    }

		    return  xmlProjectsPaths;
	    }

    }
}