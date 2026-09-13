const MAX_BYTES = 256 * 1024;
export async function fetchJson(url, signal, fetcher = fetch) {
  const response = await fetcher(url, {
    signal: AbortSignal.any([signal ?? new AbortController().signal, AbortSignal.timeout(10_000)]),
    redirect: 'error', headers: { 'User-Agent': 'F1-Help/0.1 (local desktop reference client)', 'Accept': 'application/json' }
  });
  if (!response.ok) throw new Error('Lookup service is unavailable. Try again later.');
  if (!response.body) throw new Error('Lookup service returned an empty response.');
  const reader = response.body.getReader();
  const chunks = []; let length = 0;
  try {
    for (;;) {
      const { done, value } = await reader.read(); if (done) break;
      length += value.byteLength;
      if (length > MAX_BYTES) throw new Error('Lookup response exceeded the size limit.');
      chunks.push(value);
    }
  } finally { await reader.cancel(); reader.releaseLock(); }
  try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); }
  catch { throw new Error('Lookup service returned invalid data.'); }
}
const output = (data) => ({ content: [{ type: 'text', text: JSON.stringify(data) }], details: {} });
export function weatherTool(config, fetcher = fetch, apiKey = '') {
  return {
    name: 'f1_weather', label: 'Weather', description: 'Get current weather and today’s forecast for configured coordinates or explicitly supplied latitude/longitude. If no location is known, ask the user; do not guess.',
    parameters: { type: 'object', properties: { latitude: { type: 'number', minimum: -90, maximum: 90 }, longitude: { type: 'number', minimum: -180, maximum: 180 } }, additionalProperties: false },
    async execute(_id, params, signal) {
      const lat = params.latitude ?? config.latitude, lon = params.longitude ?? config.longitude;
      if ((params.latitude == null) !== (params.longitude == null)) return output({ error: 'Supply both latitude and longitude.' });
      if (!Number.isFinite(lat) || !Number.isFinite(lon) || lat < -90 || lat > 90 || lon < -180 || lon > 180)
        return output({ error: 'Ask for a location or configure latitude and longitude in F1 Settings.' });
      const url = new URL(config.weatherEndpoint ?? 'https://api.open-meteo.com/v1/forecast');
      if (url.protocol !== 'https:' || url.username || url.password || url.search || url.hash) throw new Error('Invalid weather endpoint.');
      const source = url.origin + url.pathname;
      url.search = new URLSearchParams({ latitude: String(lat), longitude: String(lon), current: 'temperature_2m,apparent_temperature,precipitation,weather_code', daily: 'temperature_2m_max,temperature_2m_min,precipitation_probability_max', forecast_days: '1', timezone: 'auto' }).toString();
      if (apiKey) url.searchParams.set('apikey', apiKey);
      try {
        const data = await fetchJson(url, signal, fetcher);
        if (!data.current || typeof data.current.temperature_2m !== 'number' || !data.daily) throw new Error('Invalid weather data.');
        return output({ location: params.latitude == null ? config.locationLabel : undefined, current: data.current, current_units: data.current_units,
          daily: data.daily, daily_units: data.daily_units, timezone: data.timezone, retrievedAt: new Date().toISOString(),
          source, attribution: 'Weather data by Open-Meteo — https://open-meteo.com/' });
      } catch (error) {
        if (signal?.aborted) throw new Error('Lookup cancelled.');
        // Do not leak a URL containing a credential through fetch errors.
        return output({ error: 'Weather lookup failed or timed out. Check location and service settings.' });
      }
    }
  };
}
export function referenceTool(fetcher = fetch) {
  return {
    name: 'f1_reference', label: 'Reference lookup', description: 'Look up a topic on English Wikipedia. Returns a bounded introduction with attribution, or disambiguation candidates. Use when a reference is needed, not for every question.',
    parameters: { type: 'object', properties: { query: { type: 'string', minLength: 1, maxLength: 200 }, title: { type: 'string', minLength: 1, maxLength: 200 } }, required: ['query'], additionalProperties: false },
    async execute(_id, params, signal) {
      if (typeof params.query !== 'string' || params.query.trim().length === 0 || params.query.length > 200) return output({ error: 'Supply a topic under 200 characters.' });
      try {
        let title = params.title;
        if (!title) {
          const search = new URL('https://en.wikipedia.org/w/api.php');
          search.search = new URLSearchParams({ action: 'query', format: 'json', list: 'search', srsearch: params.query, srlimit: '3', srprop: '' }).toString();
          const data = await fetchJson(search, signal, fetcher);
          if (!Array.isArray(data.query?.search)) throw new Error('Invalid reference data.');
          const matches = data.query.search;
          if (matches.length === 0) return output({ error: 'No matching reference found.' });
          const exact = matches.find(m => m.title.toLowerCase() === params.query.toLowerCase());
          if (!exact && matches.length > 1) return output({ candidates: matches.map(m => m.title), instruction: 'Choose the matching topic from context, or ask the user, then call with its title.' });
          title = (exact ?? matches[0]).title;
        }
        if (typeof title !== 'string' || title.length > 200) throw new Error('Invalid title.');
        const url = new URL('https://en.wikipedia.org/w/api.php');
        url.search = new URLSearchParams({ action: 'query', format: 'json', formatversion: '2', prop: 'extracts|pageprops', exintro: '1', explaintext: '1', exchars: '2400', titles: title, redirects: '1' }).toString();
        const data = await fetchJson(url, signal, fetcher);
        const page = data.query?.pages?.[0];
        if (!page || page.missing || typeof page.extract !== 'string') return output({ error: 'Reference article is unavailable.' });
        return output({ title: page.title, excerpt: page.extract.slice(0, 2400), disambiguation: page.pageprops?.disambiguation !== undefined,
          source: 'https://en.wikipedia.org/wiki/' + encodeURIComponent(page.title.replaceAll(' ', '_')),
          attribution: 'Wikipedia contributors; CC BY-SA. Preserve the source link and identify Wikipedia as the source.' });
      } catch {
        if (signal?.aborted) throw new Error('Lookup cancelled.');
        return output({ error: 'Reference lookup failed or timed out. Try again later.' });
      }
    }
  };
}
