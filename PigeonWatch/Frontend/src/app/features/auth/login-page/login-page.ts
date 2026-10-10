import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, FormRoot, email, form, required } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { safeReturnUrl } from '../../../core/auth/safe-return-url';
import { SessionService } from '../../../core/auth/session.service';
import { ResourceService } from '../../../core/resources/resource.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import {
  Alert,
  FieldErrors,
  FormSubmitState,
  PageCard,
  SubmitButton,
  provideFormFieldDefaults,
} from '../../../shared/ui';
import { AuthFormError, authErrorMessage, loginFailure } from '../../../shared/auth/auth-errors';
import { LoginNavigationState } from '../../../shared/auth/login-navigation-state';

interface LoginModel {
  email: string;
  password: string;
}

@Component({
  providers: [provideFormFieldDefaults()],
  selector: 'app-login-page',
  imports: [
    FormField,
    FormRoot,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    RouterLink,
    TranslatePipe,
    Alert,
    FieldErrors,
    PageCard,
    SubmitButton,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login-page.html',
})
export class LoginPage {
  private readonly session = inject(SessionService);
  private readonly submitState = inject(FormSubmitState);
  private readonly router = inject(Router);
  private readonly resources = inject(ResourceService);
  private readonly returnUrl = inject(ActivatedRoute).snapshot.queryParamMap.get('returnUrl');
  private readonly navigationState = readNavigationState(this.router);
  private readonly model = signal<LoginModel>({
    email: this.navigationState?.email ?? '',
    password: '',
  });
  private readonly formError = signal<AuthFormError | null>(null);

  protected readonly loginForm = form(this.model, (path) => {
    required(path.email);
    email(path.email);
    required(path.password);
  });
  protected readonly notice = signal(this.navigationState?.notice ?? null);
  protected readonly busy = signal(false);
  protected readonly formErrorText = computed(() => {
    const error = this.formError();

    return error ? authErrorMessage(this.resources, error) : null;
  });
  protected readonly emailMessages = computed(() => ({
    required: this.resources.translate('auth.validation.required'),
    email: this.resources.translate('auth.validation.email'),
  }));
  protected readonly passwordMessages = computed(() => ({
    required: this.resources.translate('auth.validation.required'),
  }));

  protected submit(): void {
    this.submitState.markSubmitted();

    if (this.busy() || this.loginForm().invalid()) return;

    const credentials = this.model();
    this.busy.set(true);
    this.formError.set(null);
    this.session
      .login(credentials.email, credentials.password)
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: () => void this.router.navigateByUrl(safeReturnUrl(this.returnUrl)),
        error: (error: unknown) => this.formError.set(loginFailure(error)),
      });
  }
}

function readNavigationState(router: Router): LoginNavigationState | null {
  const state = router.currentNavigation()?.extras.state;

  if (!state || typeof state['email'] !== 'string' || typeof state['notice'] !== 'string') {
    return null;
  }

  return { email: state['email'], notice: state['notice'] };
}
