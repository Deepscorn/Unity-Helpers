using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UniRx;
using UnityEditor;
using UnityEngine;

namespace UnityHelpers
{
	public class ResourcesEnumerator : EditorWindow
	{
		private const string FileTemplate = @"using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace &&NAMESPACE&&
{
&&CLASSES&&
}
";
		private const string FieldTemplate = @"public const string &&FIELD-NAME&& = ""&&FIELD-VALUE&&"";\n";

		public string outputPath;
		public string pathToResourcesFolder;
		public string outputNamespace;

		//TODO
		private static readonly HashSet<char> removeChars = new HashSet<char> {'.', '!', ' '};

		[MenuItem("Unity Helpers/Enumerate Resources")]
		public static void Init()
		{
			// Get existing open window or if none, make a new one:
			var window = new ResourcesEnumerator();
			window.title = "Enumerate Resources";
			window.outputPath = EditorPrefs.GetString("UnityHelpers_GenerateResources_pathToSaveTo");
			window.pathToResourcesFolder = EditorPrefs.GetString("UnityHelpers_GenerateResources_pathToResourcesFolder");
			window.outputNamespace = EditorPrefs.GetString("UnityHelpers_GenerateResources_outputNamespace");
			if (String.IsNullOrEmpty(window.outputPath)) window.outputPath = "Scripts/GameResources.cs";
			if (String.IsNullOrEmpty(window.pathToResourcesFolder)) window.pathToResourcesFolder = "Resources";
			if (String.IsNullOrEmpty(window.outputNamespace)) window.outputNamespace = "UnityHelpers";

			window.ShowAuxWindow();
		}

		void OnGUI()
		{
			pathToResourcesFolder = EditorGUILayout.TextField("Resources Folder", pathToResourcesFolder);
			outputPath = EditorGUILayout.TextField("Output Folder", outputPath);
			outputNamespace = EditorGUILayout.TextField("Output Namespace", outputNamespace);

			if (GUILayout.Button("Enumerate", GUILayout.Height(50)))
			{
				var classes = new List<string>();
				ParseFiles("Assets/" + pathToResourcesFolder)
					.Select(c => String.Join("\n", c.Split('\n').ToList().ConvertAll(l => "\t" + l).ToArray()))
					.ObserveOnMainThread()
					.Subscribe(c =>
					{
						File.WriteAllText(Application.dataPath + "/" + outputPath,
							FileTemplate.Replace("&&CLASSES&&", c).Replace("&&NAMESPACE&&", outputNamespace));
						AssetDatabase.Refresh();
						Close();
					});
			}

			// Save the settings
			EditorPrefs.SetString("UnityHelpers_GenerateResources_pathToSaveTo", outputPath);
			EditorPrefs.SetString("UnityHelpers_GenerateResources_pathToResourcesFolder", pathToResourcesFolder);
			EditorPrefs.SetString("UnityHelpers_GenerateResources_outputNamespace", outputNamespace);
		}

		private IObservable<string> ObserveFilesExcludingMetas(string root)
		{
			return Observable.Create<string>(observer =>
			{
				var paths = Directory.GetFileSystemEntries(root);
				foreach (var path in paths)
				{
					if (Path.GetExtension(path) == ".meta")
					{
						continue;
					}
					observer.OnNext(path.Replace("\\", "/"));
				}
				observer.OnCompleted();
				return Disposable.Create(() => { });
			});
		}

		private IObservable<bool> IsDirectory(string path)
		{
			return Observable.Start(() =>
				(File.GetAttributes(path) & FileAttributes.Directory) == FileAttributes.Directory);
		}

		private string Parse(string path)
		{
			var from = Path.GetFileName(path);
			var resultBuilder = new StringBuilder();
			var makeUpper = true;
			foreach (var character in from)
			{
				if (removeChars.Contains(character))
				{
					makeUpper = true;
				}
				else
				{
					resultBuilder.Append(makeUpper ? Char.ToUpper(character) : character);
					makeUpper = false;
				}
			}
			return resultBuilder.ToString();
		}

		private string ParseSingle(string path)
		{
			var parsed = Parse(path);
			return FieldTemplate.Replace("&&FIELD-NAME&&", parsed)
				.Replace("&&FIELD-VALUE&&", path);
		}

		private string ParseMultiple(IList<string> arrayOfParsed)
		{
			var parsed = Parse(arrayOfParsed[0]);
			var resultBuilder = new StringBuilder();
			resultBuilder.Append("public class ").Append(parsed).Append("\n{\n");
			for (int i = 1; i < arrayOfParsed.Count; ++i)
			{
				resultBuilder.Append(arrayOfParsed[i]);
			}
			resultBuilder.Append("}");
			return resultBuilder.ToString();
		}

		private IObservable<string> ParseFiles(string root)
		{
			return IsDirectory(root)
				.SelectMany(isDirectory =>
				{
					if (!isDirectory)
					{
						return Observable.Return(ParseSingle(root));
					}
					return Observable.Return(root)
						.Concat(ObserveFilesExcludingMetas(root)
							.SelectMany(child => ParseFiles(child)))
						.ToList()
						.SelectMany(parsedChildren => parsedChildren.Count == 1
							? Observable.Empty<IList<string>>()
							: Observable.Return(parsedChildren))
						.Select(parsedChildren => ParseMultiple(parsedChildren));
				});
		}
	}
}