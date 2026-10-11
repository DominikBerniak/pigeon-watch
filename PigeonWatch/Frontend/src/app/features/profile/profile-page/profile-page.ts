import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  FormField,
  FormRoot,
  form,
  maxLength,
  minLength,
  required,
  validate,
} from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { catchError, filter, finalize, of, switchMap } from 'rxjs';
import { SessionService } from '../../../core/auth/session.service';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { ResourceService } from '../../../core/resources/resource.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import {
  Alert,
  FieldErrors,
  FormActions,
  FormSubmitState,
  PageCard,
  SubmitButton,
  provideFormFieldDefaults,
} from '../../../shared/ui';
import {
  ServerFieldErrors,
  displayNameCharacterValidator,
  fallbackDisplayNameRules,
  forbiddenDisplayNameCharacter,
  serverErrorFor,
} from '../../../shared/validation/account-validation';
import { AuthFormError, authErrorMessage, profileFailure } from '../../../shared/auth/auth-errors';
import { PasswordDialog } from '../password-dialog/password-dialog';
import { ProfileApi } from '../profile-api';

interface ProfileModel {
  displayName: string;
}

type ProfileField = 'displayName';

type ProfileNotice =
  | { readonly kind: 'saved' }
  | { readonly kind: 'passwordChanged' }
  | { readonly kind: 'error'; readonly error: AuthFormError };

@Component({
  providers: [provideFormFieldDefaults()],
  selector: 'app-profile-page',
  imports: [
    FormField,
    FormRoot,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    TranslatePipe,
    Alert,
    FieldErrors,
    FormActions,
    PageCard,
    SubmitButton,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './profile-page.scss',
  templateUrl: './profile-page.html',
})
export class ProfilePage {
  private readonly session = inject(SessionService);
  private readonly submitState = inject(FormSubmitState);
  private readonly configuration = inject(ConfigurationService);
  private readonly profileApi = inject(ProfileApi);
  private readonly resources = inject(ResourceService);
  private readonly dialog = inject(MatDialog);
  private readonly savedName = signal(this.session.currentUser()?.displayName ?? '');
  private readonly model = signal<ProfileModel>({ displayName: this.savedName() });
  private readonly serverErrors = signal<ServerFieldErrors<ProfileField>>({});
  private readonly notice = signal<ProfileNotice | null>(null);

  protected readonly currentUser = this.session.currentUser;
  protected readonly displayNameRules = computed(
    () => this.configuration.clientConfig()?.displayNameRules ?? fallbackDisplayNameRules,
  );
  protected readonly profileForm = form(this.model, (path) => {
    required(path.displayName);
    minLength(path.displayName, () => this.displayNameRules().minLength);
    maxLength(path.displayName, () => this.displayNameRules().maxLength);
    validate(path.displayName, displayNameCharacterValidator());
    validate(path.displayName, serverErrorFor(this.serverErrors, 'displayName'));
  });
  protected readonly busy = signal(false);
  protected readonly editing = signal(false);
  protected readonly displayName = computed(() => this.model().displayName);
  protected readonly changed = computed(
    () => this.model().displayName.trim() !== this.savedName().trim(),
  );
  protected readonly successText = computed(() => {
    const notice = this.notice();

    if (notice?.kind === 'saved') return this.resources.translate('profile.notices.saved');

    return notice?.kind === 'passwordChanged'
      ? this.resources.translate('profile.password.notices.changed')
      : null;
  });
  protected readonly errorText = computed(() => {
    const notice = this.notice();

    return notice?.kind === 'error' ? authErrorMessage(this.resources, notice.error) : null;
  });
  protected readonly displayNameMessages = computed(() => ({
    required: this.resources.translate('auth.validation.required'),
    minLength: this.resources.translate(
      'auth.validation.displayNameMinLength',
      this.displayNameRules().minLength,
    ),
    maxLength: this.resources.translate(
      'auth.validation.displayNameMaxLength',
      this.displayNameRules().maxLength,
    ),
    invalidCharacter: this.resources.translate(
      'auth.validation.displayNameInvalidCharacter',
      forbiddenDisplayNameCharacter,
    ),
    duplicateDisplayName: this.resources.translate('auth.errors.duplicateDisplayName'),
    invalidDisplayName: this.resources.translate('auth.errors.invalidDisplayName'),
  }));

  protected startEditing(): void {
    this.notice.set(null);
    this.editing.set(true);
  }

  protected changePassword(): void {
    this.notice.set(null);
    this.dialog
      .open<PasswordDialog, void, boolean>(PasswordDialog)
      .afterClosed()
      .pipe(filter((changed) => changed === true))
      .subscribe(() => this.notice.set({ kind: 'passwordChanged' }));
  }

  protected cancel(): void {
    this.model.set({ displayName: this.savedName() });
    this.profileForm().reset();
    this.serverErrors.set({});
    this.notice.set(null);
    this.editing.set(false);
  }

  protected submit(): void {
    this.submitState.markSubmitted();

    if (this.busy() || this.profileForm().invalid()) return;

    const profile = this.model();
    this.busy.set(true);
    this.notice.set(null);
    this.profileApi
      .updateDisplayName(profile.displayName)
      .pipe(
        switchMap(() => this.configuration.loadGeneral().pipe(catchError(() => of(null)))),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          const name = profile.displayName.trim();

          this.savedName.set(name);
          this.model.set({ displayName: name });
          this.notice.set({ kind: 'saved' });
          this.editing.set(false);
        },
        error: (error: unknown) => this.handleFailure(error, profile),
      });
  }

  private handleFailure(error: unknown, profile: ProfileModel): void {
    const failure = profileFailure(error);
    const kind = failure.fieldErrors.displayName;

    this.serverErrors.set(kind ? { displayName: { kind, value: profile.displayName } } : {});
    this.notice.set(failure.formError ? { kind: 'error', error: failure.formError } : null);
  }
}
