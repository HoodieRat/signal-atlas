# LinkedIn design amendment

This document supersedes the snippet-only outcome in Production Specification v1.0 §3.

Scheduled scans discover publicly indexed LinkedIn titles, URLs, and snippets through a search provider. If DokoBot discovery is unavailable, a public RSS search fallback still discovers links. With **Block LinkedIn DokoBot reads** on, scans do not fetch LinkedIn pages. With it off, they read discovered LinkedIn URLs sequentially through DokoBot local mode and feed the captured text into the normal pipeline. This setting changes only Signal Atlas behavior; it does not alter DokoBot itself.

The worker checks the setting again before each LinkedIn page read. Turning the block on during a run prevents subsequent reads; a read already in progress finishes or stops through normal cancellation.

the user may open a LinkedIn post himself, select the visible post text and any useful visible context, copy it, and choose **Capture copied content** in Signal Atlas. He provides the post URL and topic. The application imports the submitted text, stores it as a document with capture provenance, deduplicates it, queues it for the same local analysis and report pipeline, and preserves the source link.

The same setting permits a user-initiated batch of chosen LinkedIn URLs. The app invokes `dokobot read --local` once per URL, sequentially, and imports visible text returned by the extension. The UI accepts multiple pasted URLs, one per line. Browser concurrency remains one.

LinkedIn's current [prohibited software guidance](https://www.linkedin.com/help/linkedin/answer/a1341387) and [User Agreement](https://www.linkedin.com/legal/user-agreement) prohibit third-party tools and extensions that scrape or copy the service. User initiation alone is not a stated exception. The opt-out UI must state this clearly, especially before enabling scheduled reads. An official LinkedIn API adapter can be enabled when suitable access and permissions are available.

The capture UI should tell the user to submit only content he is entitled to save and use. It should never collect credentials, cookies, private messages, or hidden page data.
