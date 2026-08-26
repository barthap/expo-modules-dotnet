import * as path from 'path';

export function findPackageProjectPath(solution: string, solutionPath: string): string {
  const matches = Array.from(
    solution.matchAll(/^Project\("[^"]+"\) = "[^"]+", "([^"]+\.wapproj)", "\{[^}]+\}"/gim),
    // A solution file always separates path segments with a backslash. path.resolve
    // only treats that as a separator on Windows, so convert first or a POSIX host
    // returns one segment with an embedded backslash.
    (match) => path.resolve(path.dirname(solutionPath), match[1].split('\\').join(path.sep))
  );
  if (matches.length !== 1) {
    throw new Error(
      `[expo-modules-dotnet-autolinking] Expected exactly one .wapproj package project in ${solutionPath}, found ${matches.length}.`
    );
  }
  return matches[0];
}

export function configureMixedModeDebugger(source: string): { changed: boolean; content: string } {
  const replacement = '<DebuggerType>Mixed</DebuggerType>';
  if (source.includes(replacement)) {
    return { changed: false, content: source };
  }

  if (/<DebuggerType>[^<]*<\/DebuggerType>/.test(source)) {
    return {
      changed: true,
      content: source.replace(/<DebuggerType>[^<]*<\/DebuggerType>/, replacement),
    };
  }

  const content = source.replace(/<PropertyGroup(?:\s[^>]*)?>/, (tag) => `${tag}\n    ${replacement}`);
  if (content === source) {
    throw new Error('[expo-modules-dotnet-autolinking] WAP project does not contain a PropertyGroup.');
  }
  return { changed: true, content };
}
