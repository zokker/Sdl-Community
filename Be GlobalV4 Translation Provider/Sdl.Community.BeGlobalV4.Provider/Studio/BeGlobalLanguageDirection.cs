﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sdl.Community.BeGlobalV4.Provider.Helpers;
using Sdl.Community.BeGlobalV4.Provider.Model;
using Sdl.Community.BeGlobalV4.Provider.Service;
using Sdl.Core.Globalization;
using Sdl.LanguagePlatform.Core;
using Sdl.LanguagePlatform.TranslationMemory;
using Sdl.LanguagePlatform.TranslationMemoryApi;

namespace Sdl.Community.BeGlobalV4.Provider.Studio
{
	public class BeGlobalLanguageDirection	: ITranslationProviderLanguageDirection
	{
		private readonly BeGlobalTranslationProvider _beGlobalTranslationProvider;
		private readonly BeGlobalTranslationOptions _options;
		private readonly LanguagePair _languageDirection;
		private BeGlobalConnecter _beGlobalConnect;
		private TranslationUnit _inputTu;
		private readonly NormalizeSourceTextHelper _normalizeSourceTextHelper;
		private readonly PreTranslateTempFile _preTranslateHelp;

		public ITranslationProvider TranslationProvider => _beGlobalTranslationProvider;
		public CultureInfo SourceLanguage { get; }
		public CultureInfo TargetLanguage { get; }
		public bool CanReverseLanguageDirection { get; }

		public BeGlobalLanguageDirection(BeGlobalTranslationProvider beGlobalTranslationProvider,LanguagePair languageDirection)
		{
			_beGlobalTranslationProvider = beGlobalTranslationProvider;
			_languageDirection = languageDirection;
			_options = beGlobalTranslationProvider.Options;
			_normalizeSourceTextHelper = new NormalizeSourceTextHelper();
			_preTranslateHelp =  new PreTranslateTempFile();
		}

		public SearchResults SearchSegment(SearchSettings settings, Segment segment)
		{
			var translation = new Segment(_languageDirection.TargetCulture);
			var results = new SearchResults
			{
				SourceSegment = segment.Duplicate()
			};
			if (!_options.ResendDrafts && _inputTu.ConfirmationLevel != ConfirmationLevel.Unspecified)
			{
				translation.Add(PluginResources.TranslationLookupDraftNotResentMessage);
				//later get these strings from resource file
				results.Add(CreateSearchResult(segment, translation));
				return results;
			}
			var newseg = segment.Duplicate();
			if (newseg.HasTags)
			{
				var tagPlacer = new BeGlobalTagPlacer(newseg);
				var translatedText = LookupBeGlobal(tagPlacer.PreparedSourceText);
				translation = tagPlacer.GetTaggedSegment(translatedText);

				results.Add(CreateSearchResult(newseg, translation));
				return results;
			}
			else
			{ 
				var sourcetext = newseg.ToPlain();

				var translatedText = LookupBeGlobal(sourcetext);
				translation.Add(translatedText);

				results.Add(CreateSearchResult(newseg, translation));
				return results;
			}
		}

		public string PrepareTempData(List<PreTranslateSegment> preTranslatesegments)
		{
			try
			{
				var filePath = _preTranslateHelp.CreateXmlFile();

				for (int i = 0; i < preTranslatesegments.Count; i++)
				{
					var sourceText = string.Empty;
					var newseg = preTranslatesegments[i].TranslationUnit.SourceSegment.Duplicate();

					if (newseg.HasTags)
					{
						var tagPlacer = new BeGlobalTagPlacer(newseg);
						sourceText = tagPlacer.PreparedSourceText;
					}
					else
					{
						sourceText = newseg.ToPlain();
					}
					sourceText = _normalizeSourceTextHelper.NormalizeText(sourceText);
					_preTranslateHelp.WriteSegments(filePath, preTranslatesegments[i].Id, sourceText);
				}

				var sourceLanguage =
					_normalizeSourceTextHelper.GetCorespondingLangCode(_languageDirection.SourceCulture.ThreeLetterISOLanguageName);
				var targetLanguage =
					_normalizeSourceTextHelper.GetCorespondingLangCode(_languageDirection.TargetCulture.ThreeLetterISOLanguageName);

				var translator = new BeGlobalV4Translator("https://translate-api.sdlbeglobal.com", _options.ClientId,
					_options.ClientSecret, sourceLanguage, targetLanguage, _options.Model, _options.UseClientAuthentication);

				var translatedFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "translated.xml");
				translator.TranslateFile(filePath, translatedFile);
				return translatedFile;  
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}
			return string.Empty;
		}

		private SearchResult CreateSearchResult(Segment segment, Segment translation)
		{ 
			var tu = new TranslationUnit
			{
				SourceSegment = segment.Duplicate(),//this makes the original source segment, with tags, appear in the search window
				TargetSegment = translation
			};
			   
			tu.ResourceId = new PersistentObjectToken(tu.GetHashCode(), Guid.Empty);

			const int score = 0; //score to 0...change if needed to support scoring
			tu.Origin = TranslationUnitOrigin.MachineTranslation;
			var searchResult = new SearchResult(tu)
			{
				ScoringResult = new ScoringResult
				{
					BaseScore = score
				}
			};
			tu.ConfirmationLevel = ConfirmationLevel.Draft;

			return searchResult;
		}
	

		private string LookupBeGlobal(string sourcetext)
		{
			if (_beGlobalConnect == null)
			{ 
				_beGlobalConnect = new BeGlobalConnecter(_options.ClientId, _options.ClientSecret, _options.UseClientAuthentication,_options.Model);
			}
			else
			{
				_beGlobalConnect.ClientId = _options.ClientId;
				_beGlobalConnect.ClientSecret = _options.ClientSecret;
				_beGlobalConnect.UseClientAuthentication = _options.UseClientAuthentication;
			}

			var translatedText = _beGlobalConnect.Translate(_languageDirection, sourcetext);
			return translatedText;
		}

		public SearchResults[] SearchSegments(SearchSettings settings, Segment[] segments)
		{
			throw new NotImplementedException();
		}

		public SearchResults[] SearchSegmentsMasked(SearchSettings settings, Segment[] segments, bool[] mask)
		{
			throw new NotImplementedException();
		}

		public SearchResults SearchText(SearchSettings settings, string segment)
		{
			throw new NotImplementedException();
		}

		public SearchResults SearchTranslationUnit(SearchSettings settings, TranslationUnit translationUnit)
		{
			_inputTu = translationUnit;
			return SearchSegment(settings, translationUnit.SourceSegment);
		}

		public SearchResults[] SearchTranslationUnits(SearchSettings settings, TranslationUnit[] translationUnits)
		{
			throw new NotImplementedException();
		}

		public SearchResults[] SearchTranslationUnitsMasked(SearchSettings settings, TranslationUnit[] translationUnits,
			bool[] mask)
		{
			// bug LG-15128 where mask parameters are true for both CM and the actual TU to be updated which cause an unnecessary call for CM segment
			var results = new List<SearchResults>();
			var preTranslateList = new List<PreTranslateSegment>();

			// plugin is called from pre-translate batch task 
			//we receive the data in chunk of 10 segments
			if (translationUnits.Length > 2)
			{ 
				var i = 0;
				foreach (var tu in translationUnits)
				{
					if (mask == null || mask[i])
					{
						var preTranslate = new PreTranslateSegment
						{
							SearchSettings = settings,
							TranslationUnit = tu,
							Id = i.ToString()
						};
						preTranslateList.Add(preTranslate);
					}
					else
					{
						results.Add(null);
					}
					i++;
				}
				if (preTranslateList.Count > 0)
				{
					//Create temp file with translations
					var translatedFilePath = PrepareTempData(preTranslateList);
					if (!string.IsNullOrEmpty(translatedFilePath))
					{   //parse translated xml 
						var translationResults = _preTranslateHelp.GetTranslationFromTemp(translatedFilePath);
						if (translationResults != null)
						{
							foreach (var preTranslateSegment in preTranslateList)
							{
								var translationResult = translationResults.FirstOrDefault(s => s.Id.Equals(preTranslateSegment.Id));
								if (translationResult != null)
								{
									preTranslateSegment.PlainTranslation = translationResult.Translation;
								}
							}
						}
						var preTranslateSearchResults =GetPreTranslationSearchResults(preTranslateList);
						results.AddRange(preTranslateSearchResults);
					}
				}
			}
			else
			{
				var i = 0;
				foreach (var tu in translationUnits)
				{
					if (mask == null || mask[i])
					{
						var result = SearchTranslationUnit(settings, tu);
						results.Add(result);
					}
					else
					{
						results.Add(null);
					}
					i++;
				}
			}  
			return results.ToArray();
		}

		private List<SearchResults> GetPreTranslationSearchResults(List<PreTranslateSegment> preTranslateList)
		{
			var resultsList = new List<SearchResults>();
			foreach (var preTranslate in preTranslateList)
			{
				var translation = new Segment(_languageDirection.TargetCulture);
				var newSeg = preTranslate.TranslationUnit.SourceSegment.Duplicate();
				if (newSeg.HasTags)
				{
					var tagPlacer = new BeGlobalTagPlacer(newSeg);

					translation = tagPlacer.GetTaggedSegment(preTranslate.PlainTranslation);
					preTranslate.TranslationSegment = translation;
				}
				else
				{
					translation.Add(preTranslate.PlainTranslation);
				}  
				var searchResult = CreateSearchResult(newSeg, translation);
				var results = new SearchResults
				{
					SourceSegment = newSeg
				};
				results.Add(searchResult);
				resultsList.Add(results);
			}
			return resultsList;
		}

		public ImportResult AddTranslationUnit(TranslationUnit translationUnit, ImportSettings settings)
		{
			throw new NotImplementedException();
		}

		public ImportResult[] AddTranslationUnits(TranslationUnit[] translationUnits,
			ImportSettings settings)
		{
			throw new NotImplementedException();
		}

		public ImportResult[] AddOrUpdateTranslationUnits(TranslationUnit[] translationUnits, int[] previousTranslationHashes, ImportSettings settings)
		{
			throw new NotImplementedException();
		}

		public ImportResult[] AddTranslationUnitsMasked(TranslationUnit[] translationUnits, ImportSettings settings, bool[] mask)
		{
			throw new NotImplementedException();
		}

		public ImportResult[] AddOrUpdateTranslationUnitsMasked(TranslationUnit[] translationUnits, int[] previousTranslationHashes, ImportSettings settings, bool[] mask)
		{
			throw new NotImplementedException();
		}

		public ImportResult UpdateTranslationUnit(TranslationUnit translationUnit)
		{
			throw new NotImplementedException();
		}

		public ImportResult[] UpdateTranslationUnits(TranslationUnit[] translationUnits)
		{
			throw new NotImplementedException();
		}

	}
}