﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using IATETerminologyProvider.Helpers;
using IATETerminologyProvider.Model;
using IATETerminologyProvider.Model.ResponseModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Sdl.Core.Globalization;
using Sdl.Terminology.TerminologyProvider.Core;

namespace IATETerminologyProvider.Service
{
	public class TermSearchService
	{
		#region Private Fields
		private ProviderSettings _providerSettings;
		private ObservableCollection<ItemsResponseModel> _domains = DomainService.GetDomains();
		private List<string> _subdomains = new List<string>();
		private ObservableCollection<TermTypeModel> _termTypes = TermTypeService.GetTermTypes();
		private static int _id = new int();
		#endregion

		#region Constructors
		public TermSearchService(ProviderSettings providerSettings)
		{
			_providerSettings = providerSettings;
		}
		#endregion

		#region Public Methods
		/// <summary>
		/// Get terms from IATE database.
		/// </summary>
		/// <param name="text">text used for searching</param>
		/// <param name="source">source language</param>
		/// <param name="destination">target language</param>
		/// <param name="maxResultsCount">number of maximum results returned(set up in Studio Termbase search settings)</param>
		/// <returns>terms</returns>
		public IList<ISearchResult> GetTerms(string text, ILanguage source, ILanguage destination, int maxResultsCount)
		{
			// maxResults (the number of returned words) value is set from the Termbase -> Search Settings
			var client = new RestClient(ApiUrls.BaseUri("true", "0", maxResultsCount.ToString()));
			
			var request = new RestRequest("", Method.POST);
			request.AddHeader("Connection", "Keep-Alive");
			request.AddHeader("Cache-Control", "no-cache");
			request.AddHeader("Pragma", "no-cache");
			request.AddHeader("Accept", "application/json");
			request.AddHeader("Accept-Encoding", "gzip, deflate, br");
			request.AddHeader("Content-Type", "application/json");
			request.AddHeader("Origin", "https://iate.europa.eu");
			request.AddHeader("Host", "iate.europa.eu");			
			request.AddHeader("Access-Control-Allow-Origin", "*");

			var bodyModel = SetApiRequestBodyValues(destination, source, text);
			request.AddJsonBody(bodyModel);

			var response = client.Execute(request);
			var domainsJsonResponse = JsonConvert.DeserializeObject<JsonDomainResponseModel>(response.Content);

			var result = MapResponseValues(response, domainsJsonResponse);
			return result;
		}		
		#endregion

		#region Private Methods
		
		// Set the needed fields for the API search request
		private object SetApiRequestBodyValues(ILanguage destination, ILanguage source, string text)
		{
			var targetLanguges = new List<string>();
			var filteredDomains = new List<string>();
			var filteredTermTypes = new List<int>();

			targetLanguges.Add(destination.Locale.TwoLetterISOLanguageName);
			if (_providerSettings != null)
			{
				filteredDomains = _providerSettings.Domains.Count > 0 ? _providerSettings.Domains : filteredDomains;
				filteredTermTypes = _providerSettings.TermTypes.Count > 0 ? _providerSettings.TermTypes : filteredTermTypes;
			}

			var bodyModel = new
			{
				query = text,
				source = source.Locale.TwoLetterISOLanguageName,
				targets = targetLanguges,
				include_subdomains = true,
				filter_by_domains = filteredDomains,
				search_in_term_types = filteredTermTypes
			};
			return bodyModel;
		}

		/// <summary>
		/// Map the terms values returned from the IATE API response with the SearchResultModel
		/// </summary>
		/// <param name="response">IATE API response</param>
		/// <param name="domainResponseModel">domains response model</param>
		/// <returns>list of terms</returns>
		private IList<ISearchResult> MapResponseValues(IRestResponse response, JsonDomainResponseModel domainResponseModel)
		{
			var termsList = new List<ISearchResult>();
			if (!string.IsNullOrEmpty(response.Content))
			{
				var jObject = JObject.Parse(response.Content);
				var itemTokens = (JArray)jObject.SelectToken("items");
				if (itemTokens != null)
				{
					foreach (var item in itemTokens)
					{
						var itemId = item.SelectToken("id").ToString();
						var domainModel = domainResponseModel.Items.Where(i => i.Id == itemId).FirstOrDefault();
						var domain = SetTermDomain(domainModel);
						SetTermSubdomains(domainModel);

						_id++;
						// get language childrens (source + target languages)
						var languageTokens = item.SelectToken("language").Children().ToList();
						if (languageTokens.Any())
						{
							// foreach language token get the terms
							foreach (JProperty languageToken in languageTokens)
							{
								// Latin translations are automatically returned by IATE API response->"la" code
								// Ignore the "la" translations
								if (!languageToken.Name.Equals("la"))
								{
									var termEntry = languageToken.FirstOrDefault().SelectToken("term_entries").Last;
									var termValue = termEntry.SelectToken("term_value").ToString();
									var termType = GetTermTypeByCode(termEntry.SelectToken("type").ToString());
									var langTwoLetters = languageToken.Name;
									var definition = languageToken.Children().FirstOrDefault() != null
										? languageToken.Children().FirstOrDefault().SelectToken("definition")
										: null;

									var languageModel = new LanguageModel
									{
										Name = new Language(langTwoLetters).DisplayName,
										Locale = new Language(langTwoLetters).CultureInfo
									};

									var termResult = new SearchResultModel
									{
										Text = termValue,
										Id = _id,
										Score = 100,
										Language = languageModel,
										Definition = definition != null ? definition.ToString() : string.Empty,
										Domain = domain,
										Subdomain = FormatSubdomain(),
										TermType = termType
									};
									termsList.Add(termResult);
								}
							}
						}
					}
				}
			}
			return termsList;
		}

		// Set term main domain
		private string SetTermDomain(ItemsResponseModel itemDomains)
		{
			var domain = string.Empty;
			foreach (var itemDomain in itemDomains.Domains)
			{
				var result = _domains.Where(d => d.Code.Equals(itemDomain.Code)).FirstOrDefault();
				if (result != null)
				{
					domain = $"{result.Name}, ";
				}
			}
			return domain.TrimEnd(' ').TrimEnd(',');
		}

		// Set term subdomain
		private void SetTermSubdomains(ItemsResponseModel mainDomains)
		{
			// clear _subdomains list for each term
			_subdomains.Clear();
			if (_domains.Count > 0)
			{
				foreach (var mainDomain in mainDomains.Domains)
				{
					foreach (var domain in _domains)
					{
						// if result returns null, means that code belongs to a subdomain
						var result = domain.Code.Equals(mainDomain.Code) ? domain : null;
						if (result == null && domain.Subdomains != null)
						{
							GetSubdomainsRecursively(domain.Subdomains, mainDomain.Code, mainDomain.Note);
						}
					}
				}
			}
		}

		// Get subdomains recursively
		private void GetSubdomainsRecursively(List<SubdomainsResponseModel> subdomains, string code, string note)
		{
			foreach (var subdomain in subdomains)
			{
				if (subdomain.Code.Equals(code))
				{
					if (!string.IsNullOrEmpty(note))
					{
						var subdomainName = $"{subdomain.Name}. {note}";
						_subdomains.Add(subdomainName);
					}
					else
					{
						_subdomains.Add(subdomain.Name);
					}
				}
				else
				{
					if (subdomain.Subdomains != null)
					{
						GetSubdomainsRecursively(subdomain.Subdomains, code, note);
					}
				}
			}
		}

		// Format the subdomain in a user friendly mode.
		private string FormatSubdomain()
		{
			var result = string.Empty;
			int subdomainNo = 0;
			foreach (var subdomain in _subdomains.ToList())
			{
				subdomainNo++;
				result+= $"{ subdomainNo}.{subdomain}  ";
			}
			return result.TrimEnd(' ');
		}

		// Return the term type name based on the term type code.
		private string GetTermTypeByCode(string termTypeCode)
		{
			int result;
			int typeCode = int.TryParse(termTypeCode, out result) ? int.Parse(termTypeCode) : 0;

			if (_termTypes.Count > 0)
			{
				return _termTypes.FirstOrDefault(t => t.Code == typeCode).Name;
			}
			return string.Empty;
		}
		#endregion
	}
}