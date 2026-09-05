<script setup lang="ts">
import type { Tone } from '../lib/presentation'

defineProps<{ text: string; tone: Tone }>()

const toneClasses: Record<Tone, string> = {
  neutral: 'bg-slate-100 text-slate-700',
  info: 'bg-sky-100 text-sky-700',
  success: 'bg-emerald-100 text-emerald-700',
  warning: 'bg-amber-100 text-amber-800',
  danger: 'bg-rose-100 text-rose-700',
}
</script>

<template>
  <span class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium" :class="toneClasses[tone]">
    {{ text }}
  </span>
</template>
