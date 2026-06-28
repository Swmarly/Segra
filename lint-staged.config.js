import path from 'node:path';

const quote = (value) => `"${value.replace(/\\/g, '/').replace(/"/g, '\\"')}"`;
const node = quote(process.execPath);
const prettier = quote(path.resolve('Frontend/node_modules/prettier/bin/prettier.cjs'));
const eslint = quote(path.resolve('Frontend/node_modules/eslint/bin/eslint.js'));

export default {
  'Frontend/**/*.{ts,tsx,js,jsx,css,md,json,html}': (files) => {
    const quoted = files.map((f) => `"${f}"`).join(' ');
    return [
      `${node} ${prettier} --write ${quoted}`,
      `${node} ${eslint} --config Frontend/eslint.config.js --fix ${quoted}`,
    ];
  },
  '**/*.cs': (files) => {
    const quoted = files.map((f) => `"${f}"`).join(' ');
    // Run dotnet format on the whole solution - more reliable than per-file
    return [`dotnet format Segra.sln`];
  },
};
