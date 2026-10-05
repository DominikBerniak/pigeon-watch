import { environment } from '../../../environments/environment';

export function isApiUrl(url: string): boolean {
  return url === environment.apiUrl || url.startsWith(`${environment.apiUrl}/`);
}
