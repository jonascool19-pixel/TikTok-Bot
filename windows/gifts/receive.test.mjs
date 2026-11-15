import assert from 'node:assert/strict';
import { parseGift } from './receive.mjs';

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
