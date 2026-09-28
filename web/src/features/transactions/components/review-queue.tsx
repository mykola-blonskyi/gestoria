"use client";

import { useLocale, useTranslations } from "next-intl";
import { useId, useRef, useState, type KeyboardEvent } from "react";

import { TRANSACTION_CLASSES, type ReviewItem, type TransactionClass } from "@/data/transactions";
import type { Locale } from "@/shared/constants/locales";
import { formatDate, formatMoney } from "@/shared/lib/format";
import { Button } from "@/shared/ui/button";

import { useClassify, useReviewQueue } from "../hooks/use-transactions";
import { Failure } from "./failure";
import { kindOf } from "./kind";

const classesOf = (item: ReviewItem): TransactionClass[] => {
  const suggested = item.suggestion?.class;
  return suggested === undefined ? [...TRANSACTION_CLASSES] : [suggested, ...TRANSACTION_CLASSES.filter((c) => c !== suggested)];
};

// One movement in the tab order at a time (a roving tabindex): arrows, Home and End move between movements, Tab enters the
// focused one's buttons, and the digit shown on a button picks its class.
export function ReviewQueue({ profileId }: { profileId: string }) {
  const t = useTranslations("Transactions.review");
  const locale = useLocale() as Locale;
  const id = useId();
  const queue = useReviewQueue(profileId);
  const classify = useClassify(profileId);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [announcement, setAnnouncement] = useState("");
  const headingRef = useRef<HTMLHeadingElement>(null);
  const itemRefs = useRef(new Map<string, HTMLLIElement>());

  const items = queue.data ?? [];
  const active = items.find((item) => item.id === activeId) ?? items[0];
  const label = (c: TransactionClass) => t(`classes.${c}`);

  function focusItem(item: ReviewItem | undefined) {
    if (item === undefined) headingRef.current?.focus();
    else itemRefs.current.get(item.id)?.focus();
  }

  function pick(item: ReviewItem, index: number, transactionClass: TransactionClass) {
    if (classify.isPending) return;
    classify.mutate(
      { id: item.id, class: transactionClass },
      {
        onSuccess: () => {
          setAnnouncement(t("classified", { name: label(transactionClass) }));
          // While the class was on its way the user may have moved on (End, Home, Tab): focus stays where they put it.
          const focused = document.activeElement;
          const movedOn = focused !== null && focused !== document.body && !itemRefs.current.get(item.id)?.contains(focused);
          if (movedOn) return;
          const remaining = items.filter((other) => other.id !== item.id);
          focusItem(remaining[Math.min(index, remaining.length - 1)]);
        },
      },
    );
  }

  function onItemKeyDown(event: KeyboardEvent<HTMLLIElement>, item: ReviewItem, index: number) {
    if (event.target !== event.currentTarget || event.altKey || event.ctrlKey || event.metaKey) return;
    const moves: Record<string, number> = { ArrowDown: index + 1, ArrowUp: index - 1, Home: 0, End: items.length - 1 };
    const move = moves[event.key];
    const transactionClass = /^[1-9]$/.test(event.key) ? classesOf(item)[Number(event.key) - 1] : undefined;
    if (move !== undefined) focusItem(items[Math.max(0, Math.min(move, items.length - 1))]);
    else if (transactionClass !== undefined) pick(item, index, transactionClass);
    else return;
    event.preventDefault();
  }

  return (
    <section className="grid gap-3" aria-labelledby={`${id}-heading`}>
      <h2 id={`${id}-heading`} ref={headingRef} tabIndex={-1} className="font-medium focus:outline-2 focus:outline-offset-2 focus:outline-ring">
        {queue.isSuccess ? t("heading", { count: items.length }) : t("title")}
      </h2>
      <p role="status" className="text-sm font-medium">
        {announcement}
      </p>
      {classify.isError && <Failure error={classify.error} />}
      {queue.isPending ? (
        <p role="status">{t("loading")}</p>
      ) : queue.isError ? (
        <Failure error={queue.error} />
      ) : items.length === 0 ? (
        <p className="text-sm">{t("empty")}</p>
      ) : (
        <>
          <p id={`${id}-hint`} className="text-sm text-muted-foreground">
            {t("hint")}
          </p>
          {TRANSACTION_CLASSES.map((_, index) => (
            <span key={index} id={`${id}-key-${index + 1}`} hidden>
              {t("shortcut", { key: index + 1 })}
            </span>
          ))}
          <ul aria-labelledby={`${id}-heading`} className="grid gap-3">
            {items.map((item, index) => {
              const inTabOrder = item === active;
              const suggestionId = `${id}-${item.id}-suggestion`;
              return (
                <li
                  key={item.id}
                  ref={(element) => {
                    if (element === null) itemRefs.current.delete(item.id);
                    else itemRefs.current.set(item.id, element);
                  }}
                  tabIndex={inTabOrder ? 0 : -1}
                  aria-describedby={`${id}-hint`}
                  onFocus={() => setActiveId(item.id)}
                  onKeyDown={(event) => onItemKeyDown(event, item, index)}
                  className="grid gap-2 rounded-xl border border-border p-4 focus:outline-2 focus:outline-offset-2 focus:outline-ring"
                >
                  <div className="flex flex-wrap justify-between gap-2 text-sm">
                    <span className="text-muted-foreground">{formatDate(item.bookingDate, locale)}</span>
                    <span className="font-medium tabular-nums">{formatMoney(item.amount, locale)}</span>
                  </div>
                  <p className="break-words">{item.description}</p>
                  <p className="font-medium">{t(`question.${kindOf(item)}`)}</p>
                  {item.suggestion && (
                    <p id={suggestionId} className="text-sm">
                      {t("suggestion", { name: label(item.suggestion.class) })}
                    </p>
                  )}
                  <div className="flex flex-wrap gap-2">
                    {classesOf(item).map((transactionClass, position) => {
                      const suggested = transactionClass === item.suggestion?.class;
                      return (
                        <Button
                          key={transactionClass}
                          variant={suggested ? "primary" : "outline"}
                          tabIndex={inTabOrder ? 0 : -1}
                          aria-keyshortcuts={String(position + 1)}
                          aria-describedby={`${id}-key-${position + 1}${suggested ? ` ${suggestionId}` : ""}`}
                          onClick={() => pick(item, index, transactionClass)}
                        >
                          <kbd aria-hidden className="font-mono text-xs">
                            {position + 1}
                          </kbd>
                          {label(transactionClass)}
                        </Button>
                      );
                    })}
                  </div>
                </li>
              );
            })}
          </ul>
        </>
      )}
    </section>
  );
}
