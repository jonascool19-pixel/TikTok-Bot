import assert from 'node:assert/strict';
import { parseGift, parseAudience, roomAudience } from './receive.mjs';

const seen = new Set();
const event = {
    common: { msgId: '100', roomId: '10' },
    user: { nickname: 'Mia' },
    giftId: '42',
    gift: { name: 'Rose', type: 1 },
    repeatCount: 3,
    repeatEnd: 0
};
assert.equal(parseGift(event, '10', seen), null);
event.repeatEnd = 1;
assert.equal(parseGift(event, '11', seen), null);
assert.deepEqual(parseGift(event, '10', seen), {
    kind: 'gift', name: 'Mia', title: 'Rose', count: 3
});
assert.equal(parseGift(event, '10', seen), null);
assert.deepEqual(parseGift({
    common: { msgId: '101', roomId: '10' }, user: { displayId: 'leo' }, giftId: '43'
}, '10', seen, new Map([['43', { name: 'Stern', type: 2 }]])), {
    kind: 'gift', name: 'leo', title: 'Stern', count: 1
});
assert.deepEqual(parseAudience({ common: { roomId: '10' }, total: '72', totalUser: '150' }, '10'), { kind: 'audience', count: 72, total: 150 });
assert.deepEqual(parseAudience({ viewerCount: '0' }, '10'), { kind: 'audience', count: 0, total: null });
assert.equal(parseAudience({ common: { roomId: '20' }, total: '72' }, '10'), null);
assert.deepEqual(parseAudience({ totalUser: '150' }, '10'), { kind: 'audience', count: null, total: 150 });
assert.deepEqual(roomAudience({ data: { user_count: 27, stats: { total_user: 148 } } }), { kind: 'audience', count: 27, total: 148 });
assert.equal(roomAudience({ data: { status: 4, user_count: 27 } }), null);
