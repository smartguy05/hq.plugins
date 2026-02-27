# Web Search Plugin

A plugin to allow your agent to search the web. The sample config file below is using the Kagi search engine.

## Sample config file
```
{
	"name": "webSearch",
	"description": "A plugin search the internet",
	"tools": [
		{
			"type": "function",
			"function": {
				"name": "search_web",
				"description": "Use this function to search the internet. This plugin uses the Kagi search engine.",
				"parameters": {
					"type": "object",
					"properties": {
						"query": {
							"type": "string",
							"description": "The search query to send to the search provider."
						},
						"maxResults": {
							"type": "string",
							"description": "The maximum number of results to include in a result. The default is 5."
						}
					},
					"required": [
						"query"
					]
				}
			}	
		}
	],
	"webSearchUrl": "https://www.googleapis.com/customsearch/v1?key=your_api_key&cx=your_search_id",
	"webSearchApiKey": null
}
```

### Config Options
- webSearchUrl - the web url for the search api, using the OpenSearch1.1 specification
- webSearchApiKey - the api key to use in the header (with 'bot' authorization header). If emtpy it will not be supplied. The google api key is supplied in the query string.

## Google APIs
- Create Search Id <a href="https://developers.google.com/custom-search/v1/overview" target="_blank">Here</a>
- Create Search API Key <a href="https://developers.google.com/custom-search/v1/overview" target="_blank">Here</a>