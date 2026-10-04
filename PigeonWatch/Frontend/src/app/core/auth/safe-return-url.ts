export function safeReturnUrl(value: string | null | undefined): string {
  if (!value || !value.startsWith('/')) return '/';

  if (value.startsWith('//') || value.startsWith('/\\')) return '/';

  return value;
}
