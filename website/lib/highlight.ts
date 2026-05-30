import { createHighlighter, type Highlighter } from "shiki";

const THEME = "github-dark-default";
const LANGS = [
  "bash",
  "csharp",
  "json",
  "jsonc",
  "typescript",
  "tsx",
  "yaml",
  "xml",
  "html",
  "powershell",
  "diff",
  "text",
];

let highlighterPromise: Promise<Highlighter> | null = null;

function getHighlighter() {
  if (!highlighterPromise) {
    highlighterPromise = createHighlighter({ themes: [THEME], langs: LANGS });
  }
  return highlighterPromise;
}

const aliases: Record<string, string> = {
  cs: "csharp",
  "c#": "csharp",
  sh: "bash",
  shell: "bash",
  console: "bash",
  ts: "typescript",
  yml: "yaml",
};

/** Server-side syntax highlight to a Shiki HTML string. Always dark theme. */
export async function highlight(code: string, lang = "text") {
  const highlighter = await getHighlighter();
  const resolved = aliases[lang] ?? lang;
  const language = highlighter.getLoadedLanguages().includes(resolved)
    ? resolved
    : "text";
  return highlighter.codeToHtml(code.trimEnd(), {
    lang: language,
    theme: THEME,
  });
}
