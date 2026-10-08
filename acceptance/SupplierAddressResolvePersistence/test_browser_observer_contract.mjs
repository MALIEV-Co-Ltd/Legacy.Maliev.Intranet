// Execute the exact C#-embedded JavaScript with Node WebStreams; this is not Chromium acceptance.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';

const source = readFileSync(new URL('./SupplierAddressBrowserObserver.cs', import.meta.url), 'utf8');
function raw(name) {
  const pattern = new RegExp(`${name} = """\\r?\\n([\\s\\S]*?)\\r?\\n\\s*""";`);
  const result = source.match(pattern);
  assert.ok(result, `Missing exact embedded ${name}`);
  return result[1];
}
const lease = setTimeout(() => {
  process.stderr.write('Source-derived observer controls exceeded five-second process lease\n');
  process.exit(1);
}, 5000);
try {
  const install = raw('InstallScript');
  const controls = raw('ControlsBody');
  const evaluate = new Function(`return async () => { const install = ${install}; ${controls} }`)();
  const result = await evaluate();
  assert.equal(result.cases.length, 17);
  assert.equal(new Set(result.cases).size, 17);
  assert.equal(result.realNetworkAllocated, false);
  assert.ok(result.cases.includes('same-promise-response-args-receiver'));
  assert.ok(result.cases.includes('deadline-pending-tee-until-original-close'));
  assert.ok(result.cases.includes('wrong-method'));
  assert.ok(result.cases.includes('url-fragment'));
  process.stdout.write(JSON.stringify({ scope: 'source-derived Node WebStreams controls; no Chromium/network',
    passed: result.cases.length, cases: result.cases, pid: process.pid, executable: process.execPath,
    source: fileURLToPath(new URL('./SupplierAddressBrowserObserver.cs', import.meta.url)) }) + '\n');
} finally {
  clearTimeout(lease);
}
