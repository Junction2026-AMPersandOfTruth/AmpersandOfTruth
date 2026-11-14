import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright-core';

function parseArgs(argv) {
  let url = '';
  let out = '';
  let degraded = false;
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--url' && i + 1 < argv.length) {
      url = argv[++i];
    } else if (arg.startsWith('--url=')) {
      url = arg.slice('--url='.length);
    } else if (arg === '--out' && i + 1 < argv.length) {
      out = argv[++i];
    } else if (arg.startsWith('--out=')) {
      out = arg.slice('--out='.length);
    } else if (arg === '--degraded') {
      degraded = true;
    }
  }
  if (!url) throw new Error('Missing required --url <http://localhost:PORT>');
  if (!out) throw new Error('Missing required --out <DIR>');
  return { baseUrl: url.replace(/\/+$/, ''), outDir: out, degraded };
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function launchBrowser() {
  const primary = process.env.CHROME || '/usr/bin/google-chrome';
  const candidates = process.env.CHROME
    ? [primary]
    : [primary, '/usr/bin/chromium-browser'];

  let lastErr;
  for (const executablePath of candidates) {
    if (!process.env.CHROME && !fs.existsSync(executablePath)) continue;
    try {
      const browser = await chromium.launch({ executablePath });
      return { browser, executablePath };
    } catch (err) {
      lastErr = err;
    }
  }
  if (lastErr) throw lastErr;
  const browser = await chromium.launch({ executablePath: primary });
  return { browser, executablePath: primary };
}

async function waitForBlazorAndIdle(page, timeoutMs = 10_000) {
  await page.waitForLoadState('domcontentloaded', { timeout: timeoutMs }).catch(() => {});
  await page.waitForLoadState('networkidle', { timeout: Math.min(timeoutMs, 8_000) }).catch(() => {});
  await page
    .waitForFunction(() => Boolean(window.Blazor), null, { timeout: Math.min(timeoutMs, 8_000) })
    .catch(() => {});
}

async function hasBlazor(page) {
  return page.evaluate(() => Boolean(window.Blazor)).catch(() => false);
}

async function isVisible(page, selector, timeoutMs = 3_000) {
  try {
    await page.locator(selector).first().waitFor({ state: 'visible', timeout: timeoutMs });
    return await hasBlazor(page);
  } catch {
    return false;
  }
}

async function isNotFoundPage(page) {
  return page
    .locator('h3', { hasText: /^Not Found$/ })
    .first()
    .isVisible()
    .catch(() => false);
}

async function waitForInvestigationOutcome(page, timeoutMs) {
  const deadline = Date.now() + timeoutMs;

  const hasOutcome = async () => {
    const hasSummary = await page
      .locator('[data-testid="summary-header"]')
      .first()
      .isVisible()
      .catch(() => false);
    if (!hasSummary) return false;

    const hasResultOrEmpty = await page
      .locator(
        '[data-testid="incident-card"], [data-testid="empty-state"], [data-testid="status-banner"], [data-testid="investigate-unavailable"]',
      )
      .first()
      .isVisible()
      .catch(() => false);
    return hasResultOrEmpty;
  };

  while (Date.now() < deadline) {
    if (await hasOutcome()) {
      // If streaming is still in progress (cancel-run visible), wait for completion within remaining budget.
      const remaining = Math.max(500, deadline - Date.now());
      const stillRunning = await page
        .locator('[data-testid="cancel-run"]')
        .first()
        .isVisible()
        .catch(() => false);
      if (stillRunning) {
        await page
          .locator('[data-testid="cancel-run"]')
          .first()
          .waitFor({ state: 'hidden', timeout: remaining })
          .catch(() => {});
      }
      await waitForBlazorAndIdle(page, 5_000);
      return true;
    }
    await sleep(200);
  }
  return false;
}

async function checkHorizontalOverflow(page) {
  return page.evaluate(() => {
    const scrollWidth = document.documentElement.scrollWidth;
    const innerWidth = window.innerWidth;
    return {
      scrollWidth,
      innerWidth,
      overflow: scrollWidth > innerWidth,
    };
  });
}

async function collectBanners(page) {
  return page
    .evaluate(() => {
      const selector = [
        '[data-testid="status-banner"]',
        '[data-testid="article-error"]',
        '[data-testid="assessment-unavailable-banner"]',
        '[data-testid="sample-data-banner"]',
      ].join(', ');
      const nodes = Array.from(document.querySelectorAll(selector));
      return nodes
        .filter((el) => {
          const rect = el.getBoundingClientRect();
          return rect.width > 0 && rect.height > 0;
        })
        .map((el) => {
          const testId = el.getAttribute('data-testid') || 'banner';
          const titleEl = el.querySelector('strong, b');
          const raw = (titleEl ? titleEl.textContent : el.textContent) || '';
          const summary = raw.trim().split(/\s+/).join(' ').slice(0, 72);
          return { testId, summary };
        });
    })
    .catch(() => []);
}

const VIEWPORTS = [
  {
    name: 'desktop',
    label: '1280×720',
    contextOptions: {
      viewport: { width: 1280, height: 720 },
    },
  },
  {
    name: 'phone',
    label: '390×844 (@2x touch)',
    contextOptions: {
      viewport: { width: 390, height: 844 },
      deviceScaleFactor: 2,
      isMobile: true,
      hasTouch: true,
    },
  },
];

const STEPS = [
  {
    num: 1,
    slug: 'landing',
    title: 'Landing `/`',
    async run({ page, baseUrl, degraded }) {
      await page.goto(`${baseUrl}/`, { waitUntil: 'domcontentloaded', timeout: 15_000 });
      await waitForBlazorAndIdle(page);
      const ok = await isVisible(page, '[data-testid="topic-input"]', 3_000);
      if (!ok) return { skipped: 'missing [data-testid="topic-input"] on /' };
      if (degraded) {
        const hasBanner = await isVisible(page, '[data-testid="status-banner"]', 3_000);
        if (!hasBanner) return { skipped: 'missing [data-testid="status-banner"] on / in degraded mode' };
      }
      return {};
    },
  },
  {
    num: 2,
    slug: 'topic-chip',
    title: 'Click first `topic-chip`',
    async run({ page, state }) {
      const ok = await isVisible(page, '[data-testid="topic-chip"]', 3_000);
      if (!ok) return { skipped: 'missing [data-testid="topic-chip"]' };

      const beforeUrl = page.url();
      await page.locator('[data-testid="topic-chip"]').first().click();
      state.chipClicked = true;

      // Wait briefly for navigation or tracker mount without consuming the 2s progress window of step 3.
      await Promise.race([
        page.waitForURL((u) => u.toString() !== beforeUrl, { timeout: 1_500 }).catch(() => {}),
        page
          .locator('[data-testid="summary-header"], [data-testid="stage-1"]')
          .first()
          .waitFor({ state: 'visible', timeout: 1_500 })
          .catch(() => {}),
      ]);
      await page.waitForFunction(() => Boolean(window.Blazor), null, { timeout: 3_000 }).catch(() => {});
      return {};
    },
  },
  {
    num: 3,
    slug: 'overview',
    title: 'Wait for `summary-header` + `incident-card` or empty state',
    async run({ page, state, vpName, screensDir }) {
      if (!state.chipClicked && !page.url().includes('/investigate')) {
        const quick = await isVisible(page, '[data-testid="summary-header"]', 1_500);
        if (!quick) {
          return { skipped: 'step 2 did not open an investigation ([data-testid="summary-header"] missing)' };
        }
      }

      const start = Date.now();
      await sleep(2_000);
      const progressFile = `03-progress-${vpName}.png`;
      await page.screenshot({ path: path.join(screensDir, progressFile), fullPage: true });

      const remaining = Math.max(1_000, 90_000 - (Date.now() - start));
      const ready = await waitForInvestigationOutcome(page, remaining);
      if (!ready) {
        const hasSummary = await isVisible(page, '[data-testid="summary-header"]', 500);
        const reason = hasSummary
          ? 'timed out (90 s) waiting for [data-testid="incident-card"] or [data-testid="empty-state"]'
          : 'missing [data-testid="summary-header"]';
        return { skipped: reason, extraScreens: [progressFile] };
      }
      return { extraScreens: [progressFile] };
    },
  },
  {
    num: 4,
    slug: 'incident',
    title: 'Click first `incident-card`',
    async run({ page, baseUrl, degraded }) {
      const hasCard = await isVisible(page, '[data-testid="incident-card"]', 3_000);
      if (!hasCard) {
        if (!degraded) return { skipped: 'missing [data-testid="incident-card"]' };
        await page.goto(`${baseUrl}/investigate/winter-war/incidents/ww-01`, {
          waitUntil: 'domcontentloaded',
          timeout: 15_000,
        });
      } else {
        const card = page.locator('[data-testid="incident-card"]').first();
        const tagName = await card.evaluate((el) => el.tagName.toLowerCase());
        if (tagName === 'a') {
          await card.click();
        } else {
          const incidentLink = card.locator('a[href*="/incidents/"]').first();
          const anyLink = card.locator('a[href]').first();
          if (await incidentLink.isVisible().catch(() => false)) {
            await incidentLink.click();
          } else if (await anyLink.isVisible().catch(() => false)) {
            await anyLink.click();
          } else {
            await card.click();
          }
        }
      }

      await waitForBlazorAndIdle(page, 10_000);
      if (await isNotFoundPage(page)) {
        return { skipped: 'incident detail route returned Not Found' };
      }
      const ok = await isVisible(
        page,
        '[data-testid="incident-header"], [data-testid="diff-viewer"]',
        8_000,
      );
      if (!ok) {
        return { skipped: 'missing [data-testid="incident-header"] / [data-testid="diff-viewer"]' };
      }
      return {};
    },
  },
  {
    num: 5,
    slug: 'diff-toggle',
    title: 'Toggle `diff-view-toggle`',
    async run({ page, degraded }) {
      const ok = await isVisible(page, '[data-testid="diff-view-toggle"]', 2_500);
      if (!ok) {
        if (degraded && (await isVisible(page, '[data-testid="status-banner"]', 2_000))) {
          return {};
        }
        return { skipped: 'missing or hidden [data-testid="diff-view-toggle"]' };
      }

      const toggle = page.locator('[data-testid="diff-view-toggle"]').first();
      const inactiveBtn = toggle.locator('button[aria-pressed="false"]').first();
      const secondBtn = toggle.locator('button').nth(1);

      if (await inactiveBtn.isVisible().catch(() => false)) {
        await inactiveBtn.click();
      } else if (await secondBtn.isVisible().catch(() => false)) {
        await secondBtn.click();
      } else {
        await toggle.click();
      }

      await waitForBlazorAndIdle(page, 5_000);
      return {};
    },
  },
  {
    num: 6,
    slug: 'article',
    title: 'Follow first article link to `/articles/...`',
    async run({ page, state }) {
      let hasLink = await isVisible(page, 'a[href^="/articles/"], a[href*="/articles/"]', 2_500);
      if (!hasLink) {
        // On <992px overview, article rail may be inside a collapsed <details> disclosure.
        const closedDetails = page
          .locator('details:not([open]):has(a[href*="/articles/"]) summary')
          .first();
        if (await closedDetails.isVisible().catch(() => false)) {
          await closedDetails.click();
          hasLink = await isVisible(page, 'a[href^="/articles/"], a[href*="/articles/"]', 2_000);
        }
      }
      if (!hasLink) return { skipped: 'missing link to /articles/...' };

      await page.locator('a[href^="/articles/"], a[href*="/articles/"]').first().click();
      state.articleNavigated = true;

      await waitForBlazorAndIdle(page, 10_000);
      if (await isNotFoundPage(page)) {
        return { skipped: '/articles/... returned Not Found' };
      }
      const ok = await isVisible(
        page,
        '[data-testid="article-timeline"], [data-testid="article-page"], [data-testid="article-error"], svg.aot-chart, [data-testid="empty-state"]',
        5_000,
      );
      if (!ok) {
        return { skipped: 'missing article timeline/content selector on /articles/...' };
      }
      return {};
    },
  },
  {
    num: 7,
    slug: 'back',
    title: 'Browser back',
    async run({ page, state }) {
      if (!state.articleNavigated) {
        return { skipped: 'step 6 did not navigate to /articles/...' };
      }
      await page.goBack({ waitUntil: 'domcontentloaded', timeout: 10_000 });
      await waitForBlazorAndIdle(page, 10_000);
      const ok = await isVisible(
        page,
        '[data-testid="incident-header"], [data-testid="diff-viewer"], [data-testid="summary-header"]',
        5_000,
      );
      if (!ok) {
        return { skipped: 'previous page selector missing after browser back' };
      }
      return {};
    },
  },
  {
    num: 8,
    slug: 'mannerheim',
    title: 'Landing, type "Mannerheim" in `topic-input`, Enter',
    async run({ page, baseUrl, degraded }) {
      await page.goto(`${baseUrl}/`, { waitUntil: 'domcontentloaded', timeout: 15_000 });
      await waitForBlazorAndIdle(page);

      const hasInput = await isVisible(page, '[data-testid="topic-input"]', 3_000);
      if (!hasInput) return { skipped: 'missing [data-testid="topic-input"] on /' };

      const input = page.locator('[data-testid="topic-input"]').first();
      if (degraded && (await input.isDisabled().catch(() => false))) {
        const hasBanner = await isVisible(page, '[data-testid="status-banner"]', 2_000);
        if (!hasBanner) return { skipped: 'topic-input disabled without [data-testid="status-banner"]' };
        return {};
      }

      await input.fill('Mannerheim');
      await sleep(50);
      await input.press('Enter');

      await Promise.race([
        page.waitForURL(/\/investigate/i, { timeout: 8_000 }).catch(() => {}),
        page
          .locator(
            '[data-testid="summary-header"], [data-testid="stage-1"], [data-testid="out-of-scope"], [data-testid="empty-state"]',
          )
          .first()
          .waitFor({ state: 'visible', timeout: 8_000 })
          .catch(() => {}),
      ]);

      if (
        page.url().includes('/investigate') ||
        (await isVisible(page, '[data-testid="summary-header"], [data-testid="stage-1"]', 500))
      ) {
        const ready = await waitForInvestigationOutcome(page, 90_000);
        if (!ready) {
          return { skipped: 'search for "Mannerheim" timed out or missing summary-header + outcome' };
        }
        return {};
      }

      const inlineState = await isVisible(
        page,
        '[data-testid="out-of-scope"], [data-testid="empty-state"]',
        2_000,
      );
      if (!inlineState) {
        return { skipped: 'submitting "Mannerheim" did not navigate or show results' };
      }
      return {};
    },
  },
  {
    num: 9,
    slug: 'climate-change',
    title: 'Landing, type "climate change", Enter (out-of-scope or results)',
    async run({ page, baseUrl, degraded }) {
      await page.goto(`${baseUrl}/`, { waitUntil: 'domcontentloaded', timeout: 15_000 });
      await waitForBlazorAndIdle(page);

      const hasInput = await isVisible(page, '[data-testid="topic-input"]', 3_000);
      if (!hasInput) return { skipped: 'missing [data-testid="topic-input"] on /' };

      const input = page.locator('[data-testid="topic-input"]').first();
      if (degraded && (await input.isDisabled().catch(() => false))) {
        // Also exercise /accounts/Example in degraded mode so every dependency-backed page's banner is verified
        await page.goto(`${baseUrl}/accounts/Example`, { waitUntil: 'domcontentloaded', timeout: 15_000 });
        await waitForBlazorAndIdle(page);
        const hasAccountBanner = await isVisible(page, '[data-testid="status-banner"]', 3_000);
        if (!hasAccountBanner) return { skipped: 'missing [data-testid="status-banner"] on /accounts/Example in degraded mode' };
        return {};
      }

      await input.fill('climate change');
      await sleep(50);
      await input.press('Enter');

      await Promise.race([
        page.waitForURL(/\/investigate/i, { timeout: 8_000 }).catch(() => {}),
        page
          .locator(
            '[data-testid="out-of-scope"], [data-testid="empty-state"], [data-testid="summary-header"], [data-testid="stage-1"]',
          )
          .first()
          .waitFor({ state: 'visible', timeout: 8_000 })
          .catch(() => {}),
      ]);

      if (await isVisible(page, '[data-testid="out-of-scope"], [data-testid="empty-state"]', 1_500)) {
        await waitForBlazorAndIdle(page, 5_000);
        return {};
      }

      if (
        page.url().includes('/investigate') ||
        (await isVisible(page, '[data-testid="summary-header"], [data-testid="stage-1"]', 500))
      ) {
        const ready = await waitForInvestigationOutcome(page, 90_000);
        if (!ready) {
          return { skipped: 'search for "climate change" timed out waiting for outcome' };
        }
        return {};
      }

      return { skipped: 'submitting "climate change" did not render out-of-scope or results' };
    },
  },
  {
    num: 10,
    slug: 'methodology',
    title: '`/methodology`',
    async run({ page, baseUrl }) {
      const response = await page.goto(`${baseUrl}/methodology`, {
        waitUntil: 'domcontentloaded',
        timeout: 15_000,
      });
      await waitForBlazorAndIdle(page);

      if (response && response.status() >= 400) {
        return { skipped: `/methodology returned HTTP ${response.status()}` };
      }
      if (await isNotFoundPage(page)) {
        return { skipped: '/methodology returned Not Found' };
      }

      const ok = await isVisible(
        page,
        '[data-testid="methodology"], [data-testid="methodology-page"], [data-testid="tier-badge"]',
        3_000,
      );
      if (!ok) {
        return { skipped: 'missing data-testid ([data-testid="methodology"] or [data-testid="tier-badge"]) on /methodology' };
      }
      return {};
    },
  },
];

const NL = String.fromCharCode(10);
const CR = String.fromCharCode(13);
const BT = String.fromCharCode(96);
const BS = String.fromCharCode(92);

function inlineCode(text) {
  return BT + String(text).replaceAll(BT, "'") + BT;
}

function firstLine(err) {
  const msg = err instanceof Error ? err.message : String(err);
  return msg.replaceAll(CR, '').split(NL)[0];
}

function escapeMd(text) {
  return String(text)
    .replaceAll('|', BS + '|')
    .replaceAll(CR, '')
    .replaceAll(NL, ' ');
}

function buildReport({ baseUrl, executablePath, browserVersion, startedAtIso, degraded, results }) {
  const okCount = results.filter((r) => !r.skipped && !r.error).length;
  const skippedCount = results.filter((r) => r.skipped).length;
  const errorCount = results.filter((r) => r.error).length;
  const consoleErrTotal = results.reduce((sum, r) => sum + r.consoleErrors.length, 0);
  const failedReqTotal = results.reduce((sum, r) => sum + r.failedRequests.length, 0);
  const overflowCount = results.filter((r) => r.overflow?.overflow).length;
  const bannerStepCount = results.filter((r) => (r.banners ?? []).length > 0).length;

  const lines = [
    degraded ? '# P2.11 Degraded Review Walk Report' : '# P2.12 Review Walk Report',
    '',
    `- **UTC timestamp:** ${startedAtIso}`,
    `- **Mode:** ${degraded ? 'degraded (corpus + Ollama pointed at closed ports)' : 'standard'}`,
    `- **Target URL:** ${inlineCode(baseUrl)}`,
    `- **Browser:** ${inlineCode(executablePath)} (Chromium ${browserVersion})`,
    `- **Summary:** ${okCount}/${results.length} passed · ${skippedCount} skipped · ${errorCount} failed · ${bannerStepCount}/${results.length} steps with banners · ${consoleErrTotal} console error(s) · ${failedReqTotal} failed request(s) · ${overflowCount} horizontal scroll flag(s)`,
    '',
    '## Step × Viewport Matrix',
    '',
    '| Step | Viewport | Status | Banners | Screenshot | Timing (s) | Console / Failed Reqs | Overflow |',
    '|---|---|---|---|---|---:|---|---|',
  ];

  for (const r of results) {
    const nn = String(r.num).padStart(2, '0');
    const stepCol = `${nn} ${r.title}`;
    const statusCol = r.error
      ? `error: ${escapeMd(r.error)}`
      : r.skipped
        ? `skipped: ${escapeMd(r.skipped)}`
        : 'ok';

    const bannerCol =
      (r.banners ?? []).length > 0
        ? r.banners
            .map((b) => `${inlineCode(b.testId)}: ${escapeMd(b.summary)}`)
            .join('; ')
        : '—';

    const screenLinks = [
      ...(r.extraScreens ?? []).map((f) => `[progress](screens/${f})`),
      `[${r.screenFile}](screens/${r.screenFile})`,
    ].join(', ');

    const errSummaryParts = [];
    if (r.consoleErrors.length > 0) errSummaryParts.push(`${r.consoleErrors.length} console`);
    if (r.failedRequests.length > 0) errSummaryParts.push(`${r.failedRequests.length} req`);
    const errCol = errSummaryParts.length > 0 ? errSummaryParts.join(', ') : '0';

    const overflowCol = r.overflow?.overflow
      ? `horizontal scroll (${r.overflow.scrollWidth}px > ${r.overflow.innerWidth}px)`
      : 'ok';

    lines.push(
      `| ${escapeMd(stepCol)} | ${escapeMd(r.vpName)} (${escapeMd(r.vpLabel)}) | ${statusCol} | ${bannerCol} | ${screenLinks} | ${r.durationSec.toFixed(2)} | ${errCol} | ${overflowCol} |`,
    );
  }

  const noisy = results.filter((r) => r.consoleErrors.length > 0 || r.failedRequests.length > 0);
  if (noisy.length > 0) {
    lines.push('', '## Console Errors & Failed Requests', '');
    for (const r of noisy) {
      const nn = String(r.num).padStart(2, '0');
      lines.push(`### Step ${nn} (${r.slug}) — ${r.vpName}`);
      for (const msg of r.consoleErrors) {
        lines.push(`- **console:** ${inlineCode(msg)}`);
      }
      for (const req of r.failedRequests) {
        lines.push(`- **request:** ${inlineCode(req)}`);
      }
      lines.push('');
    }
  }

  return lines.join(NL) + NL;
}

async function main() {
  const { baseUrl, outDir, degraded } = parseArgs(process.argv.slice(2));
  const screensDir = path.join(outDir, 'screens');
  fs.mkdirSync(screensDir, { recursive: true });

  const startedAtIso = new Date().toISOString();
  const { browser, executablePath } = await launchBrowser();
  const browserVersion = browser.version();
  const results = [];

  try {
    for (const vp of VIEWPORTS) {
      console.log(`▶ Viewport: ${vp.name} (${vp.label})${degraded ? ' [degraded]' : ''}`);
      const context = await browser.newContext(vp.contextOptions);
      const page = await context.newPage();

      let currentConsoleErrors = [];
      let currentFailedRequests = [];

      page.on('console', (msg) => {
        if (msg.type() === 'error') {
          currentConsoleErrors.push(msg.text());
        }
      });
      page.on('pageerror', (err) => {
        currentConsoleErrors.push(`pageerror: ${err.message}`);
      });
      page.on('requestfailed', (req) => {
        const errText = req.failure()?.errorText ?? 'failed';
        if (errText.includes('ERR_ABORTED')) return;
        currentFailedRequests.push(`${req.method()} ${req.url()} (${errText})`);
      });
      page.on('response', (res) => {
        if (res.status() >= 500) {
          currentFailedRequests.push(`HTTP ${res.status()} ${res.request().method()} ${res.url()}`);
        }
      });

      const state = {
        chipClicked: false,
        articleNavigated: false,
      };

      for (const step of STEPS) {
        currentConsoleErrors = [];
        currentFailedRequests = [];
        const t0 = performance.now();
        const nn = String(step.num).padStart(2, '0');
        const screenFile = `${nn}-${step.slug}-${vp.name}.png`;

        let outcome = {};
        let stepError = '';
        try {
          outcome = await step.run({
            page,
            baseUrl,
            degraded,
            state,
            vpName: vp.name,
            screensDir,
          });
        } catch (err) {
          // Unexpected selector/navigation error: log as skipped/degraded and keep walking.
          outcome = { skipped: firstLine(err) };
        }

        const banners = await collectBanners(page);

        try {
          await page.screenshot({
            path: path.join(screensDir, screenFile),
            fullPage: true,
          });
        } catch (err) {
          stepError = firstLine(err);
        }

        const overflow = await checkHorizontalOverflow(page).catch(() => ({
          scrollWidth: 0,
          innerWidth: 0,
          overflow: false,
        }));

        const durationSec = (performance.now() - t0) / 1000;

        if (outcome.skipped) {
          console.log(`  step ${step.num} skipped: ${outcome.skipped}`);
        } else if (stepError) {
          console.log(`  step ${step.num} error: ${stepError}`);
        } else {
          const ovNote = overflow.overflow ? ' [HORIZONTAL SCROLL]' : '';
          const bannerNote = banners.length > 0 ? ` [${banners.map((b) => b.testId).join(', ')}]` : '';
          console.log(`  step ${step.num} (${step.slug}): ok in ${durationSec.toFixed(2)}s${bannerNote}${ovNote}`);
        }

        results.push({
          num: step.num,
          slug: step.slug,
          title: step.title,
          vpName: vp.name,
          vpLabel: vp.label,
          skipped: outcome.skipped ?? '',
          error: stepError,
          banners,
          screenFile,
          extraScreens: outcome.extraScreens ?? [],
          durationSec,
          consoleErrors: [...currentConsoleErrors],
          failedRequests: [...currentFailedRequests],
          overflow,
        });
      }

      await context.close();
    }
  } finally {
    await browser.close();
  }

  const reportPath = path.join(outDir, 'report.md');
  const report = buildReport({
    baseUrl,
    executablePath,
    browserVersion,
    startedAtIso,
    degraded,
    results,
  });
  fs.writeFileSync(reportPath, report, 'utf8');
  console.log(`✅ Report written to ${reportPath}`);

  if (degraded) {
    const consoleErrTotal = results.reduce((sum, r) => sum + r.consoleErrors.length, 0);
    const failedReqTotal = results.reduce((sum, r) => sum + r.failedRequests.length, 0);
    const hasDegradedBanners = results.some((r) =>
      (r.banners ?? []).some((b) => b.testId === 'status-banner' || b.testId === 'article-error'),
    );
    if (consoleErrTotal > 0 || failedReqTotal > 0 || !hasDegradedBanners) {
      throw new Error(
        `Degraded walk failed assertions: consoleErrors=${consoleErrTotal}, failedRequests=${failedReqTotal}, hasDegradedBanners=${hasDegradedBanners}`,
      );
    }
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
