<script setup>
import { ref, onMounted } from 'vue';
import { getCatalogs, getBookings } from '../api.js';

const catalogs = ref([]);
const bookings = ref([]);
const error = ref(null);
const loading = ref(false);

async function refresh() {
  loading.value = true;
  error.value = null;
  try {
    const [c, b] = await Promise.all([getCatalogs(), getBookings()]);
    catalogs.value = c;
    bookings.value = b;
  } catch (e) {
    error.value = e.message;
  } finally {
    loading.value = false;
  }
}

const short = (id) => (id ? String(id).slice(0, 8) : '');
const money = (amt, ccy) => `${Number(amt).toFixed(2)} ${ccy}`;

onMounted(refresh);
defineExpose({ refresh }); // App calls this after a confirmed action
</script>

<template>
  <section class="result">
    <div class="head">
      <span>Results</span>
      <button :disabled="loading" @click="refresh">{{ loading ? '…' : 'Refresh' }}</button>
    </div>

    <div class="body">
      <p v-if="error" class="error">⚠️ {{ error }}</p>

      <h3>Catalogs <span class="count">{{ catalogs.length }}</span></h3>
      <p v-if="!catalogs.length" class="empty">No catalogs yet.</p>
      <ul class="list">
        <li v-for="c in catalogs" :key="c.id" class="card">
          <div class="row">
            <strong>{{ c.title }}</strong>
            <span class="badge" :class="c.isAvailable ? 'ok' : 'off'">
              {{ c.isAvailable ? 'Available' : 'Unavailable' }}
            </span>
          </div>
          <div class="muted">{{ c.description }}</div>
          <div class="meta">{{ money(c.pricePerNight, c.currency) }} / night · <code>{{ short(c.id) }}</code></div>
        </li>
      </ul>

      <h3>Bookings <span class="count">{{ bookings.length }}</span></h3>
      <p v-if="!bookings.length" class="empty">No bookings yet.</p>
      <ul class="list">
        <li v-for="b in bookings" :key="b.id" class="card">
          <div class="row">
            <strong><code>{{ short(b.id) }}</code></strong>
            <span class="badge status">{{ b.status }}</span>
          </div>
          <div class="meta">{{ b.checkIn }} → {{ b.checkOut }} · {{ money(b.amount, b.currency) }}</div>
          <div class="muted">catalog <code>{{ short(b.catalogId) }}</code></div>
        </li>
      </ul>
    </div>
  </section>
</template>

<style scoped>
.result {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.6rem 0.9rem;
  font-weight: 600;
  border-bottom: 1px solid var(--border);
}

.head button {
  font-size: 0.8rem;
  padding: 0.3rem 0.7rem;
  border: 1px solid var(--border);
  border-radius: 7px;
  background: #fff;
}

.body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 0.9rem;
}

h3 {
  font-size: 0.9rem;
  margin: 1rem 0 0.5rem;
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

h3:first-child {
  margin-top: 0;
}

.count {
  font-size: 0.72rem;
  color: var(--muted);
  background: #eef1f4;
  border-radius: 999px;
  padding: 0.05rem 0.5rem;
}

.list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.card {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 0.55rem 0.7rem;
}

.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
}

.muted {
  color: var(--muted);
  font-size: 0.85rem;
}

.meta {
  font-size: 0.85rem;
  margin-top: 0.15rem;
}

code {
  background: #f1f3f5;
  border-radius: 4px;
  padding: 0 0.3rem;
  font-size: 0.82em;
}

.badge {
  font-size: 0.72rem;
  border-radius: 999px;
  padding: 0.1rem 0.55rem;
  white-space: nowrap;
}

.badge.ok {
  background: #e7f6ec;
  color: var(--ok);
}

.badge.off {
  background: #f3e7e7;
  color: #b91c1c;
}

.badge.status {
  background: var(--accent-soft);
  color: var(--accent);
}

.empty {
  color: var(--muted);
  margin: 0.2rem 0;
  font-size: 0.9rem;
}

.error {
  color: #b91c1c;
}
</style>
