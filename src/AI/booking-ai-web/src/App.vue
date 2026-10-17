<script setup>
import { ref } from 'vue';
import ChatPane from './components/ChatPane.vue';
import ResultPane from './components/ResultPane.vue';

const resultPane = ref(null);

// The chat pane raises this after a booking/cancel is confirmed, so the result pane reloads.
function onChanged() {
  resultPane.value?.refresh();
}
</script>

<template>
  <div class="app">
    <header class="topbar">
      <span class="logo">🏨</span>
      <h1>Booking AI Assistant</h1>
    </header>

    <main class="panes">
      <ChatPane class="pane" @changed="onChanged" />
      <ResultPane ref="resultPane" class="pane" />
    </main>
  </div>
</template>

<style scoped>
.app {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.topbar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1.25rem;
  background: var(--panel);
  border-bottom: 1px solid var(--border);
}

.topbar h1 {
  font-size: 1.05rem;
  margin: 0;
}

.logo {
  font-size: 1.25rem;
}

.panes {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
  padding: 1rem;
}

.pane {
  min-height: 0;
  background: var(--panel);
  border: 1px solid var(--border);
  border-radius: 10px;
  overflow: hidden;
}

@media (max-width: 800px) {
  .panes {
    grid-template-columns: 1fr;
  }
}
</style>
