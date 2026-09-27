import { cn } from "cn";
import type { ComponentProps } from "react";

const VARIANTS = {
  primary: "bg-primary text-primary-foreground",
  outline: "border border-input bg-transparent",
} as const;

export function Button({ className, variant = "primary", type = "button", ...props }: ComponentProps<"button"> & { variant?: keyof typeof VARIANTS }) {
  return (
    <button
      data-slot="button"
      type={type}
      className={cn(
        "inline-flex h-9 items-center justify-center gap-2 rounded-lg px-4 text-sm font-medium outline-none focus-visible:ring-3 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-50",
        VARIANTS[variant],
        className,
      )}
      {...props}
    />
  );
}
