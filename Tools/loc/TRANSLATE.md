# Translation brief for "One More Mile" (mobile endless-runner action game)

Input: `Tools/loc/keys.tsv` — one line per string: `id <TAB> kind <TAB> context <TAB> source`.
- `source` is mostly Japanese; some are English UI labels or English data texts (character names/subtitles/flavor, card names).
- `kind`: `ui` (screen text), `scene` (fixed screen labels), `card` (card name or card effect description), `char` (character name / subtitle / flavor text / role), `stage` (map name / map description lines).
- `context` is the source file and line or the data id — use it to understand where the text appears. These are all things a normal player sees.

Output: for each target language write `Tools/loc/out/<code>.tsv` (UTF-8), **one line per input id, in the same order, every id present**:
`id <TAB> translation`
No header, no extra columns, no quotes, no tabs or line breaks inside a translation.

## Rules (important)
1. **Meaning first.** For cards, never change the trigger condition, the effect, the numbers, or the drawback/risk. Keep "1Lvごとに+3%" style numbers exactly ("+3% per Lv").
2. **Placeholders**: keep every `{0}`, `{1}`, … exactly (same numbers, each once). You may move them to where the grammar needs them.
3. **Rich text tags**: keep `<color=#xxxxxx>…</color>`, `<size=NN>…</size>`, `<b>…</b>` exactly and balanced; translate only the text between them.
4. **Symbols** stay: ★ ☆ ▶ ◀ ▼ ▲ × → ・ ≡ % + / : ( ) [ ] and numbers like 1,000m / km/h / Lv.9.
5. **Keep these game terms in Latin capitals (do not translate)**: MILE, HP, EXP, Lv / Lv., BEST, MASTERY / Mastery, AWAKENED, ULTIMATE, FINAL EVOLUTION, COMBO, BONUS ZONE, JACKPOT, BREAK, GO, CO-OP, VERSUS, HOST, JOIN, LAN, MAX, OK, ON, OFF, DEBUG, km/h, m. Also the game title "One More Mile". Card-category words inside descriptions that are already English stay English.
6. **Names**: card names (English capitals like "ATTACK UP"), character names ("SWORDSMAN"), and big English banners ("LEVEL UP", "GAME CLEAR", "BOSS REWARD") — translate them naturally into the target language in the same punchy style (capitals for Latin-script languages). Map names: 荒野街道 = Wasteland Road, 自然洞窟 = Natural Cave, 天空回廊 = Sky Corridor, 闘技場 = Arena — translate consistently.
7. **Length**: UI space is tight. Aim for at most ~1.3× the length of a natural English version; prefer short, common UI wording (buttons: 1–3 words). Use the informal/friendly register usual for mobile games in that language.
8. Japanese-specific phrasing (e.g. 「」) → use the target language's normal quotes. Do not add explanations or notes.
9. If a source line is just a symbol/number-like fragment, copy it unchanged.
10. Consistency: translate the same Japanese term the same way everywhere (カード=card, デッキ=deck, 合成=fusion, 所持=owned, 解放=unlock, 脱出=escape, ラン=run, ボス=boss, 死神=Reaper, 練習=practice, ガチャ=gacha).

### Japanese output (`ja.tsv`) — special
For **ja**, output a line for every id too, but:
- Japanese source lines: copy the source unchanged.
- English UI labels: give natural Japanese for functional labels (BACK→戻る, SELECT→決定, DECK→デッキ, COLLECTION→コレクション, SETTINGS→設定, START→スタート …), but **keep stylized English** for: card names, character names, character role badges, big banners/headlines (LEVEL UP, GAME CLEAR, FAILED, BOSS REWARD, NEW CARD, BONUS ZONE!, JACKPOT…), and the game terms in rule 5.
- English character subtitles/flavor texts: translate into natural Japanese.

### Right-to-left languages (ar, fa)
Write normal logical-order text (as you would type it). The game reorders it for display.

### Self-check before finishing
Count lines = number of ids in keys.tsv; every id present once; placeholders and tags match the source on every line.
