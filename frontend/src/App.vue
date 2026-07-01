<script setup lang="ts">
import { RouterView } from 'vue-router'
import AppHeader from './components/AppHeader.vue'
</script>

<template>
  <div class="h-full flex flex-col">
    <AppHeader />
    <main class="flex-1 overflow-auto">
      <RouterView />
    </main>
  </div>
</template>
