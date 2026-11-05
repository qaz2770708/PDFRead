import {defineConfig} from 'vite';
import tailwindcss from '@tailwindcss/vite';
export default defineConfig({base:'./',resolve:{preserveSymlinks:true},plugins:[tailwindcss()],build:{chunkSizeWarningLimit:650,rollupOptions:{onwarn(warning,warn){if(warning.code==='MODULE_LEVEL_DIRECTIVE'&&warning.message.includes('use client'))return;warn(warning);}},outDir:'dist',emptyOutDir:true,target:'es2022'}});
