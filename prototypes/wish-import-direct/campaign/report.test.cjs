const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

for (const [script, count, first] of [["report.cjs", 100, 1], ["report-controls.cjs", 5, 101]]) {
  test(`${script} renders product text safely and refuses to replace an existing report`, (t) => {
    const repo = path.resolve(__dirname, "../../..");
    const parent = path.join(repo, ".local");
    fs.mkdirSync(parent, { recursive: true });
    const directory = fs.mkdtempSync(path.join(parent, "report-security-"));
    t.after(() => {
      const cleanup = fs.realpathSync(directory);
      assert.equal(path.dirname(cleanup), fs.realpathSync(parent));
      fs.rmSync(cleanup, { recursive: true, force: true });
    });
    const campaignFile = path.join(directory, "campaign.jsonl");
    const imagesFile = path.join(directory, "images.jsonl");
    const outputFile = path.join(directory, "report.md");
    const campaign = Array.from({ length: count }, (_, index) => ({ id: index + first, merchant: "Fixture", url: "https://example.test/product", name: "Back\\slash | [link] <img>", price: "1.25", durationMs: 1 }));
    fs.writeFileSync(campaignFile, campaign.map(JSON.stringify).join(os.EOL));
    fs.writeFileSync(imagesFile, campaign.map(row => JSON.stringify({ id: row.id, decoded: true, category: "decoded" })).join(os.EOL));
    const args = [path.join(__dirname, script), campaignFile, imagesFile, outputFile];
    const written = spawnSync(process.execPath, args, { encoding: "utf8" });
    assert.equal(written.status, 0, written.stderr);
    const report = fs.readFileSync(outputFile, "utf8");
    assert.ok(report.includes("Back&#92;slash &#124; &#91;link&#93; &#60;img&#62;"));
    const refused = spawnSync(process.execPath, args, { encoding: "utf8" });
    assert.notEqual(refused.status, 0);
    assert.equal(fs.readFileSync(outputFile, "utf8"), report);
  });
}
