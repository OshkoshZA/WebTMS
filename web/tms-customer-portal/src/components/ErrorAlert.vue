<script setup lang="ts">
defineProps<{ message: string }>()
</script>

<template>
  <div class="rounded-md border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-800">
    {{ message }}
  </div>
</template>
