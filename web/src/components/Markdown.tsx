import { marked } from "marked";
import { useMemo } from "react";
import { cx } from "../lib/cx";

marked.setOptions({ gfm: true, breaks: false });

export function Markdown({ text, streaming = false, className }: { text: string; streaming?: boolean; className?: string }) {
  const html = useMemo(() => {
    // [ref_id:3] -> superscript citation chip
    const withCites = text.replace(/\[ref_id:(\d+)\]/g, '<sup class="cite">$1</sup>');
    return marked.parse(withCites, { async: false });
  }, [text]);
  return <div className={cx("md", streaming && "caret", className)} dangerouslySetInnerHTML={{ __html: html }} />;
}
