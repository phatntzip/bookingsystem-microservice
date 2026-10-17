<script setup>
import { ref, nextTick } from 'vue';
import { sendChat, confirmProposal } from '../api.js';

const emit = defineEmits(['changed']);

const messages = ref([]); // { role: 'user' | 'assistant' | 'note', text }
const input = ref('');
const conversationId = ref(null);
const pending = ref(null); // { proposalId, action, summary }
const busy = ref(false);
const scroller = ref(null);

async function scrollDown() {
  await nextTick();
  scroller.value?.scrollTo({ top: scroller.value.scrollHeight });
}

function push(role, text) {
  messages.value.push({ role, text });
  scrollDown();
}

async function send() {
  const text = input.value.trim();
  if (!text || busy.value) return;
  input.value = '';
  push('user', text);
  busy.value = true;
  try {
    const res = await sendChat(text, conversationId.value);
    conversationId.value = res.conversationId;
    if (res.assistantMessage) push('assistant', res.assistantMessage);
    pending.value = res.proposal ?? null;
  } catch (e) {
    push('note', `⚠️ ${e.message}`);
  } finally {
    busy.value = false;
  }
}

async function decide(approve) {
  if (!pending.value || busy.value) return;
  const proposal = pending.value;
  pending.value = null;
  busy.value = true;
  try {
    const res = await confirmProposal(conversationId.value, proposal.proposalId, approve);
    if (res.status === 'executed') {
      push('note', `✅ ${proposal.action} executed. ${res.result ?? ''}`);
      emit('changed'); // refresh the result pane
    } else if (res.status === 'cancelled') {
      push('note', `🚫 ${proposal.action} cancelled.`);
    } else {
      push('note', JSON.stringify(res));
    }
  } catch (e) {
    push('note', `⚠️ ${e.message}`);
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <section class="chat">
    <div class="head">Conversation</div>

    <div ref="scroller" class="log">
      <p v-if="!messages.length" class="empty">
        Ask me to find a catalog or make a booking — e.g.
        <em>"Book catalog &lt;id&gt; from tomorrow for 2 nights"</em>.
      </p>
      <div v-for="(m, i) in messages" :key="i" class="msg" :class="m.role">
        <span class="who">{{ m.role === 'user' ? 'You' : m.role === 'assistant' ? 'Assistant' : '' }}</span>
        <div class="bubble">{{ m.text }}</div>
      </div>
    </div>

    <div v-if="pending" class="proposal">
      <div class="proposal-title">Confirm required</div>
      <div class="proposal-summary">{{ pending.summary }}</div>
      <div class="proposal-actions">
        <button class="confirm" :disabled="busy" @click="decide(true)">Confirm</button>
        <button class="decline" :disabled="busy" @click="decide(false)">Decline</button>
      </div>
    </div>

    <form class="composer" @submit.prevent="send">
      <input v-model="input" :disabled="busy" placeholder="Type a message…" autocomplete="off" />
      <button type="submit" :disabled="busy || !input.trim()">{{ busy ? '…' : 'Send' }}</button>
    </form>
  </section>
</template>

<style scoped>
.chat {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.head {
  padding: 0.6rem 0.9rem;
  font-weight: 600;
  border-bottom: 1px solid var(--border);
}

.log {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 0.9rem;
  display: flex;
  flex-direction: column;
  gap: 0.7rem;
}

.empty {
  color: var(--muted);
  margin: 0;
}

.msg {
  display: flex;
  flex-direction: column;
  max-width: 85%;
}

.msg.user {
  align-self: flex-end;
  align-items: flex-end;
}

.msg.note {
  align-self: center;
  max-width: 95%;
}

.who {
  font-size: 0.72rem;
  color: var(--muted);
  margin-bottom: 0.15rem;
}

.bubble {
  white-space: pre-wrap;
  line-height: 1.4;
  padding: 0.55rem 0.75rem;
  border-radius: 10px;
  background: #f1f3f5;
}

.msg.user .bubble {
  background: var(--accent);
  color: #fff;
}

.msg.note .bubble {
  background: var(--accent-soft);
  color: var(--text);
  font-size: 0.9rem;
}

.proposal {
  margin: 0 0.9rem;
  padding: 0.7rem 0.85rem;
  border: 1px solid #fcd9a8;
  background: #fff7ec;
  border-radius: 10px;
}

.proposal-title {
  font-weight: 600;
  color: var(--warn);
  font-size: 0.85rem;
}

.proposal-summary {
  margin: 0.25rem 0 0.6rem;
  font-size: 0.92rem;
}

.proposal-actions {
  display: flex;
  gap: 0.5rem;
}

.proposal-actions .confirm {
  background: var(--ok);
  color: #fff;
  border: none;
  border-radius: 7px;
  padding: 0.4rem 0.9rem;
}

.proposal-actions .decline {
  background: #fff;
  color: var(--text);
  border: 1px solid var(--border);
  border-radius: 7px;
  padding: 0.4rem 0.9rem;
}

.composer {
  display: flex;
  gap: 0.5rem;
  padding: 0.75rem;
  border-top: 1px solid var(--border);
}

.composer input {
  flex: 1;
  padding: 0.55rem 0.7rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  font: inherit;
}

.composer button {
  padding: 0.55rem 1.1rem;
  border: none;
  border-radius: 8px;
  background: var(--accent);
  color: #fff;
}
</style>
