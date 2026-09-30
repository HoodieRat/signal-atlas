# Signal Atlas walkthrough and verified outputs

[Watch the captioned walkthrough](https://hoodierat.github.io/signal-atlas/), [download the MP4](https://hoodierat.github.io/signal-atlas/demo/Signal-Atlas-Live-Demo.mp4?v=howto-v2), or read the [transcript](Signal-Atlas-Live-Demo-Transcript.md). Separate [SRT](Signal-Atlas-Live-Demo.srt) and [WebVTT](Signal-Atlas-Live-Demo.vtt) captions are available.

The video shows actual captures of the Signal Atlas Windows app. It walks through browser setup, finding and choosing an installed LM Studio model, a **successful local Qwen3 4B test**, topics and sources, output options, and the manual run entry point. This local test loaded the model, returned a structured analysis, and unloaded it. No OpenAI API key was used. Narration was produced with ElevenLabs Bella; the credential stays outside this repository.

The cards and report in the last part of the video are **real outputs from a separate completed run using Codex with ChatGPT**. They illustrate what the app produces; they are not represented as output from the LM Studio test. That run used an isolated Signal Atlas profile, retrieved three public Epic Games sources on September 29, 2026, analyzed all three, and finished with zero failed items. The report contains its research question, evidence limits, citations, findings, and references. Report quality and length depend on the selected model and evidence.

Sources retrieved for the published example:

- [Unreal Engine 5.8 is now available](https://www.unrealengine.com/news/unreal-engine-5-8-is-now-available), Epic Games, June 23, 2026.
- [Download the latest Game Animation Sample Project, updated for UE 5.8](https://www.unrealengine.com/tech-blog/download-the-latest-game-animation-sample-project-now-updated-for-ue-5-8), Epic Games, August 12, 2026.
- [5.8.3 Hotfix Released](https://forums.unrealengine.com/t/5-8-3-hotfix-released/2833315), Epic Developer Community, September 22, 2026.

Inspect the actual [PDF](Unreal-Engine-5.8-Live-Report.pdf), [narrative HTML report](Unreal-Engine-5.8-Live-Report.html), and [card digest](Unreal-Engine-5.8-Live-Cards.html). The two standalone HTML copies have only artifact navigation removed. Private profile data and credentials are excluded from the repository and video.
