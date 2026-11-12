import assert from 'node:assert/strict';
import test from 'node:test';
import { getLiveActions } from '../src/live-actions.js';

test('an ongoing gift streak is silent and the final count is used for thanks', () => {
  const rules = { gift: { enabled: true, speak: true, chatTemplate: 'Danke, {name}!' } };
  const event = { type: 'gift', name: 'Alex', gift: 'Rose', count: 5 };
  assert.deepEqual(getLiveActions({ ...event, streakPending: true }, rules), []);
  assert.deepEqual(getLiveActions(event, rules), [
    { type: 'speak', text: 'Danke, Alex, für 5 × Rose!' },
    { type: 'chat', text: 'Danke, Alex!' },
  ]);
});

test('ignores the bot, disabled events, and comments containing links', () => {
  const rules = { comment: { enabled: true, speak: true } };
  assert.deepEqual(getLiveActions({ type: 'comment', userId: 'bot', text: 'Hi' }, rules, 'bot'), []);
  assert.deepEqual(getLiveActions({ type: 'join', name: 'Alex' }, rules), []);
  assert.deepEqual(getLiveActions({ type: 'comment', text: 'https://example.com' }, rules), []);
});

test('a configured recording can play alongside a written greeting', () => {
  const rules = { join: { enabled: true, clipId: 'welcome', chatTemplate: 'Hallo {name}' } };
  assert.deepEqual(getLiveActions({ type: 'join', name: 'Alex' }, rules), [
    { type: 'clip', clipId: 'welcome' },
    { type: 'chat', text: 'Hallo Alex' },
  ]);
});
