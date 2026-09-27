import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

// Next 16.3's `next dev` writes AGENTS.md and CLAUDE.md into web/ when it detects a coding agent;
// the repository's agent instructions live at its root instead.
const nextConfig: NextConfig = { agentRules: false };

export default createNextIntlPlugin("./src/i18n/request.ts")(nextConfig);
