// concord-maia-bridge — injected agent for Maia's WebView panel.
//
// This file is loaded as a string by cdp_injected.py at module-import time
// and evaluated INSIDE Maia's WebView via Chrome DevTools Protocol. It
// installs window.__maiaBridge, an event-driven helper for sending prompts
// and detecting completion. The Python side polls window.__maiaBridge.poll().
//
// LOADING CONTRACT:
//   - cdp_injected.py reads this file with Path(...).read_text(encoding='utf-8')
//   - It is then passed verbatim to _evaluate(), which auto-wraps in
//     (() => { <body> })().
//   - This file MUST be a sequence of statements with explicit `return`s for
//     the wrapped IIFE to surface a value. Do NOT wrap in your own IIFE here.
//
// RETURN CONTRACT:
//   - 'already-installed'      — re-injection on a WebView that already has the agent
//   - 'chat-root-not-found'    — Maia panel structure unrecognizable
//   - 'installed'              — fresh install completed
//
// (Kept as a real .js file rather than a Python triple-quoted string so that
// JS string literals containing '\n' don't get silently converted to literal
// newlines by Python's string handling — a real bug we hit in v0.0.x.)

if (window.__maiaBridge && window.__maiaBridge.version === 1) {
    return 'already-installed';
}

// Find the message-list container by walking up from the input.
// Probed structure on Studio Pro 11.10 (2026-05-07):
//   FORM (input) → SECTION → DIV.wrapper{ header, message-list, SECTION-with-form }
// The chat list is the wrapper's middle sibling — NOT the small header,
// NOT the section containing the form. Identified by being the largest
// non-trivial sibling. Works in both fresh-chat (welcome screen) and
// populated-chat states without requiring existing message bubbles.
function findChatRoot() {
    const ta = document.getElementById('MX_CHAT_INPUT');
    if (!ta) return null;
    let n = ta;
    for (let depth = 0; depth < 10 && n.parentElement; depth++) {
        n = n.parentElement;
        const parent = n.parentElement;
        if (!parent || parent.children.length < 2) continue;
        // Pick the largest non-input sibling — the message list will have
        // the most text content of any non-input sibling, even in welcome
        // state ("Welcome to Your AI Assistant..." > "Chat Learn New chat").
        let best = null;
        let bestLen = 0;
        for (const sib of parent.children) {
            if (sib === n) continue;
            if (sib.tagName === 'SCRIPT' || sib.tagName === 'STYLE') continue;
            const len = (sib.innerText || '').length;
            if (len > bestLen) { best = sib; bestLen = len; }
        }
        if (best && bestLen > 0) return best;
    }
    return null;
}

const tickets = new Map();   // sentinel -> {status, response, sent_at, completed_at, last_text_seen, stable_since}

// Bounded-growth policy for the tickets Map. A long-running Concord session
// driving hundreds of maia__send calls would otherwise leak every sentinel
// forever. TTL + count-cap with oldest-first eviction.
const TICKET_TTL_MS = 60 * 60 * 1000;   // 1 hour — well past any real round-trip
const TICKET_MAX_COUNT = 100;

function pruneTickets() {
    const now = Date.now();
    // TTL pass: drop tickets older than the TTL regardless of status. A
    // ticket older than an hour is either lost (Maia never replied) or stale
    // (caller forgot to forget()) — neither case wants to pin memory.
    for (const [sentinel, t] of tickets) {
        if (now - t.sent_at > TICKET_TTL_MS) {
            tickets.delete(sentinel);
        }
    }
    // Cap pass: if still over the count limit, drop oldest first.
    if (tickets.size > TICKET_MAX_COUNT) {
        const sorted = [...tickets.entries()].sort(
            (a, b) => a[1].sent_at - b[1].sent_at
        );
        const toDelete = tickets.size - TICKET_MAX_COUNT;
        for (let i = 0; i < toDelete; i++) {
            tickets.delete(sorted[i][0]);
        }
    }
}

const chatRoot = findChatRoot();
if (!chatRoot) {
    return 'chat-root-not-found';
}

// Extract Maia's reply by walking DOM, not by slicing innerText.
// The chat is a sequence of <p> bubbles. The user-echo bubble is the one
// containing the sentinel; Maia's reply is everything in <p> bubbles AFTER
// that. innerText-based slicing picks up bubble metadata (page-context
// tags, "Maia" label) that aren't part of Maia's actual answer.
function extractReply(sentinel) {
    const ps = [...chatRoot.querySelectorAll('p')];
    if (!ps.length) return '';
    let firstSentinelIdx = -1;
    let lastSentinelIdx = -1;
    for (let i = 0; i < ps.length; i++) {
        const t = ps[i].innerText || '';
        if (t.includes(sentinel)) {
            if (firstSentinelIdx === -1) firstSentinelIdx = i;
            lastSentinelIdx = i;
        }
    }
    if (firstSentinelIdx === -1) return '';
    // If Maia echoed: lastSentinelIdx > firstSentinelIdx, take p's between
    // them. If Maia didn't echo: lastSentinelIdx === firstSentinelIdx,
    // take p's AFTER firstSentinelIdx.
    const start = firstSentinelIdx + 1;
    const end = lastSentinelIdx > firstSentinelIdx ? lastSentinelIdx : ps.length;
    const replyParts = [];
    for (let i = start; i < end; i++) {
        replyParts.push(ps[i].innerText || '');
    }
    return replyParts.join('\n').split(sentinel)[0].trim();
}

// Whenever the chat content changes, scan pending tickets:
//   1. If sentinel appears >=2 times → done (user echo + Maia echo)
//   2. Else if Maia's bubble has been stable for 2.5s AND the user
//      echo (count>=1) has landed → done (Maia gave a short answer and
//      omitted the sentinel echo, which she sometimes does)
function scanForCompletions() {
    const text = chatRoot.innerText || '';
    const now = Date.now();
    for (const [sentinel, t] of tickets) {
        if (t.status === 'done') continue;
        let count = 0;
        let idx = 0;
        while ((idx = text.indexOf(sentinel, idx)) !== -1) {
            count++; idx += sentinel.length;
        }
        if (count >= 2) {
            t.response = extractReply(sentinel);
            t.status = 'done';
            t.completed_at = now;
            t.completion_reason = 'sentinel-echo';
            continue;
        }
        if (count >= 1) {
            t.status = 'streaming';
            if (t.last_text_seen === text) {
                if (t.stable_since && (now - t.stable_since) > 2500) {
                    t.response = extractReply(sentinel);
                    t.status = 'done';
                    t.completed_at = now;
                    t.completion_reason = 'stable-no-sentinel';
                }
            } else {
                t.last_text_seen = text;
                t.stable_since = now;
            }
        }
    }
}

const observer = new MutationObserver(scanForCompletions);
observer.observe(chatRoot, { childList: true, subtree: true, characterData: true });

// Run scanForCompletions on a timer too — MutationObserver doesn't fire
// when the content is just sitting still, so we need periodic checks
// for the stability fallback to actually trigger.
// Also prune tickets opportunistically; pruning is O(n) and runs every 500ms
// which is plenty even for a session at the cap (100 entries → ~0.1ms work).
const timer = setInterval(() => { scanForCompletions(); pruneTickets(); }, 500);

window.__maiaBridge = {
    version: 1,
    chatRoot,
    tickets,
    submit(prompt, sentinel) {
        const ta = document.getElementById('MX_CHAT_INPUT');
        if (!ta) return { ok: false, error: 'no-input' };
        const setter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value').set;
        const folded = prompt.replace(/\s+/g, ' ').trim();
        // Sentinel instruction is folded — Maia sometimes ignores multi-line
        // instructions but a single inline directive lands. The stability-
        // fallback in scanForCompletions handles the case where Maia answers
        // and forgets to echo (common for short replies).
        const full = folded + ' Respond, then write ' + sentinel + ' on a new line so the pipeline knows you are done.';
        setter.call(ta, full);
        ta.dispatchEvent(new Event('input', { bubbles: true }));
        ta.focus();
        ta.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true }));
        tickets.set(sentinel, { status: 'pending', response: '', sent_at: Date.now() });
        return { ok: true };
    },
    poll(sentinel) {
        const t = tickets.get(sentinel);
        if (!t) return { unknown: true };
        return {
            status: t.status,
            response: t.response || '',
            elapsed_ms: Date.now() - t.sent_at,
            completed_ms: t.completed_at ? (t.completed_at - t.sent_at) : null,
        };
    },
    forget(sentinel) {
        return tickets.delete(sentinel);
    },
    list() {
        return [...tickets.entries()].map(([s, t]) => ({ sentinel: s, status: t.status, elapsed_ms: Date.now() - t.sent_at }));
    },
    teardown() {
        observer.disconnect();
        clearInterval(timer);
        delete window.__maiaBridge;
    },
};

return 'installed';
