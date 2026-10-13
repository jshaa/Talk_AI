// Single source of truth shared with the training notebook: a fine-tuned model only behaves as trained
// when the app sends exactly the same system prompt.
import template from '../../../../training/system_prompt.txt?raw';

export const SYSTEM_PROMPT_TEMPLATE = template.trim();

export function systemPrompt(name: string): string {
  return SYSTEM_PROMPT_TEMPLATE.replaceAll('{name}', name.trim());
}
